namespace LinQL.GraphQL.Client.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LinQL;
using global::GraphQL.Client.Abstractions;
using global::GraphQL.Client.Http;
using global::GraphQL.Client.Serializer.SystemTextJson;
using HotChocolate.Data;
using LinQL.GraphQL.Client.Tests.Filtering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Client = LinQL.GraphQL.Client.Tests.Filtering;

/// <summary>
/// Sends filters built from the generated client to a real Hot Chocolate server with
/// <c>AddFiltering()</c>, so the generated extensions are exercised end to end.
/// </summary>
public class FilterTests : IDisposable
{
    private static readonly string[] Banana = ["banana"];

    private static readonly string[] BananaId = ["2"];

    private static readonly string[] BananaOrCherry = ["banana", "cherry"];

    private static readonly string[] TaggedNew = ["apple", "banana"];

    // Expression trees cannot contain collection expressions, so the sort input is a static field.
    private static readonly Client.ProductSortInput[] PriceDescending = [new() { Price = Client.SortEnumType.DESC }];

    private readonly WebApplication server;
    private readonly IGraphQLClient client;

    public FilterTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting().AddGraphQLServer().AddQueryType<ProductQueryType>().AddFiltering().AddSorting();

        this.server = builder.Build();
        this.server.UseRouting().UseEndpoints(e => e.MapGraphQL());
        this.server.StartAsync().GetAwaiter().GetResult();

        var testServer = this.server.GetTestServer();

        this.client = new GraphQLHttpClient(
            new GraphQLHttpClientOptions { HttpMessageHandler = testServer.CreateHandler(), EndPoint = new Uri(testServer.BaseAddress, "/graphql") },
            // results returns an interface, so its implementation has to be registered for deserialisation.
            new SystemTextJsonSerializer(new JsonSerializerOptions().RegisterInterface<Client.SearchResult>(typeof(Client.Product))))
            .WithLinQL(new LinQLOptions());
    }

    [Fact]
    public async Task ScalarOperationFiltersRows()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.Products.Filter(f => f.Name(n => n.Contains("an"))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(Banana);
    }

    [Fact]
    public async Task OrKeepsOrSemantics()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.Products.Filter(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("banana")))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(BananaOrCherry);
    }

    [Fact]
    public async Task SomeOnCollectionFiltersRows()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.Products.Filter(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(TaggedNew);
    }

    [Fact]
    public async Task PagingArgumentIsForwarded()
    {
        // The resolver takes first from the whole set and the filter is applied to that, so the number
        // of rows that survive shows the value reached the server: two candidates leave banana, three
        // also leave cherry. A dropped argument would return all three either way.
        var two = await this.client.SendAsync(
            (Client.Query q) => q.Featured.First(2).Filter(f => f.Price(p => p.Gt(10))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        var three = await this.client.SendAsync(
            (Client.Query q) => q.Featured.First(3).Filter(f => f.Price(p => p.Gt(10))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        two.Errors.Should().BeNull();
        three.Errors.Should().BeNull();
        two.Data.Select(p => p.Name).Should().BeEquivalentTo(Banana);
        three.Data.Select(p => p.Name).Should().BeEquivalentTo(BananaOrCherry);
    }

    [Fact]
    public async Task SortingArgumentIsForwarded()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q
                .Featured
                .First(3)
                .Filter(f => f.Price(p => p.Gt(10)))
                .Order(PriceDescending)
                .Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().Equal("cherry", "banana");
    }

    [Fact]
    public async Task FilterOnInterfaceResolverFiltersRows()
    {
        // The filter is a field argument, so whether the resolver returns an interface should not matter.
        var result = await this.client.SendAsync(
            (Client.Query q) => q.Results.Filter(f => f.Name(n => n.Contains("an"))).Select(r => new { r.Id }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(r => r.Id).Should().BeEquivalentTo(BananaId);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.server.StopAsync().GetAwaiter().GetResult();
        this.server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public class ProductQueryType
    {
        [UseFiltering]
        public IQueryable<Product> GetProducts() => Data.AsQueryable();

        [UseFiltering]
        public IQueryable<SearchResult> GetResults() => Data.AsQueryable();

        [UseFiltering]
        [UseSorting]
        public IQueryable<Product> GetFeatured(int first) => Data.AsQueryable().Take(first);
    }

    public interface SearchResult
    {
        string Id { get; }

        string Name { get; }
    }

    public class Product : SearchResult
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }

        public List<Tag> Tags { get; set; } = [];
    }

    public class Tag
    {
        public string Label { get; set; } = string.Empty;
    }

    private static readonly List<Product> Data =
    [
        new() { Id = "1", Name = "apple", Price = 5, Tags = [new() { Label = "new" }, new() { Label = "sale" }] },
        new() { Id = "2", Name = "banana", Price = 15, Tags = [new() { Label = "new" }] },
        new() { Id = "3", Name = "cherry", Price = 25, Tags = [] },
    ];
}
