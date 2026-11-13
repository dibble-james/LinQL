namespace LinQL.GraphQL.Client.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using global::GraphQL.Client.Abstractions;
using global::GraphQL.Client.Http;
using global::GraphQL.Client.Serializer.SystemTextJson;
using HotChocolate.Data;
using LinQL.Description;
using LinQL.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Sends filters built by LinQL to a Hot Chocolate server with <c>AddFiltering()</c> and checks the rows.
/// The builder types here stand in for the ones the generator will emit.
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
            (FilterQuery q) => q.FilterProducts(f => f.Name(n => n.Contains("an"))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(Banana);
    }

    [Fact]
    public async Task OrKeepsOrSemantics()
    {
        var result = await this.client.SendAsync(
            (FilterQuery q) => q.FilterProducts(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("banana")))).Select(p => new { p.Name }),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Select(p => p.Name).Should().BeEquivalentTo(BananaOrCherry);
    }

    [Fact]
    public async Task SomeOnCollectionFiltersRows()
    {
        var result = await this.client.SendAsync(
            (FilterQuery q) => q.FilterProducts(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }),
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
        new() { Name = "apple", Price = 5, Tags = [new() { Label = "new" }, new() { Label = "sale" }] },
        new() { Name = "banana", Price = 15, Tags = [new() { Label = "new" }] },
        new() { Name = "cherry", Price = 25, Tags = [] },
    ];

    [OperationType(RootOperationType.Query)]
    public class FilterQuery : RootType<FilterQuery>
    {
        public ProductRow[] Products { get; set; } = [];

        [GraphQLFilter(Field = "products", Argument = "where", GQLType = "ProductFilterInput")]
        public ProductRow[] FilterProducts(Expression<Func<ProductFilterInputBuilder, ProductFilterInputBuilder>> _) => this.Products;
    }

    public class ProductRow
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ProductFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public ProductFilterInputBuilder Name(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "price")]
        public ProductFilterInputBuilder Price(Func<NumberFilterInputBuilder, NumberFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "tags")]
        public ProductFilterInputBuilder Tags(Func<TagListFilterInputBuilder, TagListFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "or", IsList = true)]
        public ProductFilterInputBuilder Or(Func<ProductFilterInputBuilder, ProductFilterInputBuilder> _) => this;
    }

    public class StringFilterInputBuilder
    {
        [GraphQLFilterField(Name = "eq")]
        public StringFilterInputBuilder Eq(string _) => this;

        [GraphQLFilterField(Name = "contains")]
        public StringFilterInputBuilder Contains(string _) => this;
    }

    public class NumberFilterInputBuilder
    {
        [GraphQLFilterField(Name = "gt")]
        public NumberFilterInputBuilder Gt(decimal _) => this;
    }

    public class TagListFilterInputBuilder
    {
        [GraphQLFilterField(Name = "some")]
        public TagListFilterInputBuilder Some(Func<TagFilterInputBuilder, TagFilterInputBuilder> _) => this;
    }

    public class TagFilterInputBuilder
    {
        [GraphQLFilterField(Name = "label")]
        public TagFilterInputBuilder Label(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;
    }
}
