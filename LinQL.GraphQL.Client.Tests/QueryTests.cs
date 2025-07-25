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
    private readonly TestServer server;
    private readonly IGraphQLClient client;

    public QueryTests()
    {
        var hostBuilder = new WebHostBuilder()
            .ConfigureServices(services => services.AddRouting().AddGraphQLServer().AddQueryType<TestQueryType>().AddMutationType<TestMutationType>())
            .Configure(app => app.UseRouting().UseEndpoints(e => e.MapGraphQL()))
            .ConfigureLogging(logging => logging.AddDebug());

        this.server = new TestServer(hostBuilder);

        this.client = new GraphQLHttpClient(
            new GraphQLHttpClientOptions { HttpMessageHandler = this.server.CreateHandler(), EndPoint = new Uri(this.server.BaseAddress, "/graphql") },
            new SystemTextJsonSerializer())
            .WithLinQL(new LinQLOptions());
    }

    [Fact]
    public async Task TestSendQuery()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.GetNumber());

        result.Errors.Should().BeNull();
        result.Data.Should().Be(12345);
    }

    [Fact]
    public async Task TestSendProjectionQuery()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.ExecuteEchoOperation("echo").Project(e => new { e.Echo, e.Number }));

        result.Errors.Should().BeNull();
        result.Data.Number.Should().Be(42);
        result.Data.Echo.Should().Be("echo");
    }

    [Fact]
    public async Task TestSendMutation()
    {
        var result = await this.client.SendAsync((TestMutation m) => m.SetNumberOperation(54321));

        result.Errors.Should().BeNull();
        result.Data.Number.Should().Be(54321);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.server.Dispose();
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

        public TestObjectType EchoOperation { get; set; }
    }

    [OperationType(RootOperationType.Mutation)]
    public class TestMutation : RootType<TestMutation>
    {
        public TestMutationType.NumberResult SetNumber { get; set; }

        [GraphQLOperation, GraphQLField(Name = "setNumber")]
        public TestMutationType.NumberResult SetNumberOperation([GraphQLArgument(GQLType = "Int!")] int input) => this.SetNumber;
    }
}
