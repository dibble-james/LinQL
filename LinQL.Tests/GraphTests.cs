namespacenamespace LinQL.Tests;

using LinQL;

public class GraphTests
{
    private readonly IGraphQLConnection connection;
    private readonly StubGraph target;

    public GraphTests()
    {
        this.connection = Substitute.For<IGraphQLConnection>();
        this.target = new StubGraph(this.connection);
    }

    [Fact]
    public async Task FromRawGraphQLSendsValidRequest()
    {
        var expectedQuery = "This is a query";
        var expectedVariables = new Dictionary<string, object>();
        var expectedData = new object();

        this.connection.SendRequest<object>(Arg.Any<GraphQLRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GraphQLResponse<object>(expectedData, Enumerable.Empty<GraphQLError>()));

        (await this.target.FromRawGraphQL<object>(expectedQuery, expectedVariables)).Should().Be(expectedData);

        await this.connection.Received().SendRequest<object>(Arg.Is<GraphQLRequest>(x => x.Query == expectedQuery && x.Variables == expectedVariables), Arg.Any<CancellationToken>());
    }

    private class StubGraph : Graph
    {
        public StubGraph(IGraphQLConnection connection) : base(connection, Substitute.For<ILogger<Graph>>(), null!)
        
        }
    }
}
