namespace LinQL.GraphQL.Client.Tests;

using System;
using System.Threading.Tasks;
using global::GraphQL.Client.Abstractions;
using global::GraphQL.Client.Http;
using global::GraphQL.Client.Serializer.SystemTextJson;
using LinQL.Description;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public class QueryTests : IDisposable
{
    private readonly WebApplication server;
    private readonly IGraphQLClient client;

    public QueryTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting().AddGraphQLServer().AddQueryType<TestQueryType>().AddMutationType<TestMutationType>();
        builder.Logging.AddDebug();

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
    public async Task TestSendQuery()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Should().Be(12345);
    }

    [Fact]
    public async Task TestSendProjectionQuery()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.ExecuteEchoOperation("echo").Project(e => new { e.Echo, e.Number }), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Number.Should().Be(42);
        result.Data.Echo.Should().Be("echo");
    }

    [Fact]
    public async Task TestSendMutation()
    {
        var result = await this.client.SendAsync((TestMutation m) => m.SetNumberOperation(54321), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeNull();
        result.Data.Number.Should().Be(54321);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.server.StopAsync().GetAwaiter().GetResult();
        this.server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public class TestQueryType
    {
        public int GetNumber() => 12345;

        public TestObjectType EchoOperation(string toEcho) => new(toEcho, 42);
    }

    public record TestObjectType(string Echo, int Number);

    public class TestMutationType
    {
        public NumberResult SetNumber([GraphQLArgument(GQLType = "Int")] int input) => new() { Number = input };

        public class NumberResult
        {
            public int Number { get; set; }
        }
    }

    [OperationType(RootOperationType.Query)]
    public class TestQuery : RootType<TestQuery>
    {
        public int Number { get; set; }

        [GraphQLOperation, GraphQLField(Name = "number")]
        public int GetNumber() => this.Number;

        [GraphQLOperation, GraphQLField(Name = "echoOperation")]
        public TestObjectType ExecuteEchoOperation([GraphQLArgument(GQLType = "String!")] string toEcho)
            => this.EchoOperation;

        public TestObjectType EchoOperation { get; set; } = null!;
    }

    [OperationType(RootOperationType.Mutation)]
    public class TestMutation : RootType<TestMutation>
    {
        public TestMutationType.NumberResult SetNumber { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "setNumber")]
        public TestMutationType.NumberResult SetNumberOperation([GraphQLArgument(GQLType = "Int!")] int input) => this.SetNumber;
    }
}
