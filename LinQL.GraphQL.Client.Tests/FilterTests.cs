namespace LinQL.GraphQL.Client.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using global::GraphQL.Client.Abstractions;
using global::GraphQL.Client.Http;
using global::GraphQL.Client.Serializer.SystemTextJson;
using HotChocolate.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Client = LinQL.GraphQL.Client.Tests.Filtering;

/// <summary>
/// Sends filters built from the generated client to a real Hot Chocolate server with
/// <c>AddFiltering()</c>, so the generated builders are exercised end to end.
/// </summary>
public class FilterTests : IDisposable
{
    private static readonly string[] Banana = ["banana"];

    private static readonly string[] BananaOrCherry = ["banana", "cherry"];

    private static readonly string[] TaggedNew = ["apple", "banana"];

    private readonly WebApplication server;
    private readonly IGraphQLClient client;

    public FilterTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting().AddGraphQLServer().AddQueryType<ProductQueryType>().AddFiltering();

        this.server = builder.Build();
        this.server.UseRouting().UseEndpoints(e => e.MapGraphQL());
        this.server.StartAsync().GetAwaiter().GetResult();

        var testServer = this.server.GetTestServer();

        this.client = new GraphQLHttpClient(
            new GraphQLHttpClientOptions { HttpMessageHandler = testServer.CreateHandler(), EndPoint = new Uri(testServer.BaseAddress, "/graphql") },
            new SystemTextJsonSerializer())
            .WithLinQL(new LinQLOptions());
    }

    [Fact]
    public async Task ScalarOperationFiltersRows()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.FilterProducts(f => f.Name(n => n.Contains("an"))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(Banana);
    }

    [Fact]
    public async Task OrKeepsOrSemantics()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.FilterProducts(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("banana")))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(BananaOrCherry);
    }

    [Fact]
    public async Task SomeOnCollectionFiltersRows()
    {
        var result = await this.client.SendAsync(
            (Client.Query q) => q.FilterProducts(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(TaggedNew);
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
    }

    public class Product
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
