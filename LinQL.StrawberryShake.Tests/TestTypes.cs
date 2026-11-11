namespace LinQL.StrawberryShake.Tests;

using LinQL.Description;

[OperationType(RootOperationType.Query)]
public class TestQuery : RootType<TestQuery>
{
    public int Number { get; set; }

    public TestFilm Film { get; set; } = new(string.Empty, 0);

    public TestObject EchoOperation { get; set; } = new(string.Empty, 0);

    [GraphQLOperation, GraphQLField(Name = "number")]
    public int GetNumber() => this.Number;

    [GraphQLOperation, GraphQLField(Name = "film")]
    public TestFilm GetFilm([GraphQLArgument(GQLType = "ID!")] string id) => this.Film;

    [GraphQLOperation, GraphQLField(Name = "echoOperation")]
    public TestObject ExecuteEchoOperation([GraphQLArgument(GQLType = "String!")] string toEcho) => this.EchoOperation;
}

[OperationType(RootOperationType.Mutation)]
public class TestMutation : RootType<TestMutation>
{
    public TestNumberResult SetNumber { get; set; } = new(0);

    [GraphQLOperation, GraphQLField(Name = "setNumber")]
    public TestNumberResult SetNumberOperation([GraphQLArgument(GQLType = "Int!")] int input) => this.SetNumber;
}

[OperationType(RootOperationType.Subscription)]
public class TestSubscription : RootType<TestSubscription>
{
    public int Counter { get; set; }

    [GraphQLOperation, GraphQLField(Name = "counter")]
    public int GetCounter() => this.Counter;
}

public record TestFilm(string Title, int Episode);

public record TestObject(string Echo, int Number);

public record TestNumberResult(int Number);
