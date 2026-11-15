namespace LinQL.StrawberryShake.Tests;

using System.Linq.Expressions;
using System.Text;
using FluentAssertions;
using global::StrawberryShake;
using LinQL.Description;
using Xunit;

public class QueryTests
{
    private static readonly Expression<Func<TestQuery, int>> Number = q => q.Number;

    private static readonly Expression<Func<TestQuery, TestFilm>> Film = q => q.Film;

    private static readonly Expression<Func<TestQuery, string, TestObject>> Echo = (q, toEcho) => q.ExecuteEchoOperation(toEcho);

    private static readonly Expression<Func<TestSubscription, int>> Counter = s => s.GetCounter();
    [Fact]
    public async Task SendScalarQuery()
    {
        var connection = new StubConnection("{\"data\":{\"number\":12345}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(12345);
        result.Query.Should().StartWith("query linql {");

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Name.Should().Be("linql");
        Encoding.UTF8.GetString(request.Document.Body).Should().StartWith("query linql {");
    }

    [Fact]
    public async Task SendObjectQuery()
    {
        var connection = new StubConnection("{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestFilm("A New Hope", 4));
    }

    [Fact]
    public async Task SendMutation()
    {
        var connection = new StubConnection("{\"data\":{\"setNumber\":{\"number\":54321}}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestMutation m) => m.SetNumberOperation(54321), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Number.Should().Be(54321);
    }

    [Fact]
    public async Task SendsVariablesAndNamedOperation()
    {
        var connection = new StubConnection("{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}");
        var client = connection.WithLinQL();

        await client.SendAsync((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken);

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Name.Should().Be("linql");
        request.Document.Kind.Should().Be(OperationKind.Query);
        Encoding.UTF8.GetString(request.Document.Body).Should().StartWith("query linql(");
        request.Variables["var1"].Should().Be("1");
    }

    [Fact]
    public async Task SendDeclaredQuery()
    {
        var connection = new StubConnection("{\"data\":{\"number\":12345}}");
        var client = connection.WithLinQL();

        var query = LinQLQuery<TestQuery, int>.Bind(
            "query linql { __typename number }",
            RootOperationType.Query,
            Number);

        var result = await client.SendAsync(query, TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(12345);
        result.Query.Should().Be("query linql { __typename number }");

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Name.Should().Be("linql");
        request.Document.Kind.Should().Be(OperationKind.Query);
        Encoding.UTF8.GetString(request.Document.Body).Should().Be("query linql { __typename number }");
    }

    [Fact]
    public void ProjectionDropsTheDeclarationsParameters()
    {
        // A declaration's parameters are document values, so the projection a response is read through takes the
        // root alone and the argument the body passes through no longer reaches it.
        var query = LinQLQuery<TestQuery, TestObject>.Bind(
            "query linql($var1: String!) { __typename echoOperation(toEcho: $var1) { __typename echo } }",
            RootOperationType.Query,
            Echo,
            ("var1", "hello"));

        var projection = query.Projection;

        projection.Parameters.Should().ContainSingle();
        projection.Compile()(new TestQuery { EchoOperation = new TestObject("hello", 1) })
            .Should().Be(new TestObject("hello", 1));

        query.Variables["var1"].Should().Be("hello");
    }

    [Fact]
    public async Task SendDeclaredQuerySendsBoundVariables()
    {
        var connection = new StubConnection("{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}");
        var client = connection.WithLinQL();

        var query = LinQLQuery<TestQuery, TestFilm>.Bind(
            "query linql($var1: ID!) { __typename film(id: $var1) { __typename title episode } }",
            RootOperationType.Query,
            Film,
            ("var1", "1"));

        var result = await client.SendAsync(query, TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestFilm("A New Hope", 4));

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Variables.Should().NotBeNull();
        request.Variables!["var1"].Should().Be("1");
    }

    [Fact]
    public async Task MapsErrors()
    {
        var connection = new StubConnection(
            "{\"data\":null,\"errors\":[{\"message\":\"boom\",\"code\":\"E1\",\"path\":[\"film\"],\"locations\":[{\"line\":2,\"column\":3}]}]}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken);

        result.HasErrors.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Be("boom");
        result.Errors[0].Code.Should().Be("E1");
        result.Errors[0].Path.Should().Equal("film");
        result.Errors[0].Locations.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    public async Task MapsExtensions()
    {
        var connection = new StubConnection("{\"data\":{\"number\":1},\"extensions\":{\"traceId\":\"abc\"}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        result.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task MapsTransportExceptions()
    {
        var connection = new StubConnection(new HttpRequestException("down"));
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Be("down");
        result.Errors[0].Exception.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task RejectsSubscriptionsOnSendAsync()
    {
        var connection = new StubConnection("{\"data\":{\"counter\":1}}");
        var client = connection.WithLinQL();

        var send = () => client.SendAsync((TestSubscription s) => s.GetCounter());

        await send.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task WatchEmitsEveryResponse()
    {
        var connection = new StubConnection(
            "{\"data\":{\"counter\":1}}",
            "{\"data\":{\"counter\":2}}");
        var client = connection.WithLinQL();

        var values = new List<int>();
        var completed = new TaskCompletionSource();
        using var subscription = client
            .Watch((TestSubscription s) => s.GetCounter(), cancellationToken: TestContext.Current.CancellationToken)
            .Subscribe(new CollectingObserver(values, completed));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        values.Should().Equal(1, 2);
        connection.Requests.Should().ContainSingle().Which.Document.Kind.Should().Be(OperationKind.Subscription);
    }

    [Fact]
    public async Task WatchEmitsEveryResponseForADeclaredQuery()
    {
        var connection = new StubConnection(
            "{\"data\":{\"counter\":1}}",
            "{\"data\":{\"counter\":2}}");
        var client = connection.WithLinQL();

        var values = new List<int>();
        var completed = new TaskCompletionSource();
        using var subscription = client
            .Watch(DeclaredCounter(), TestContext.Current.CancellationToken)
            .Subscribe(new CollectingObserver(values, completed));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        values.Should().Equal(1, 2);

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Name.Should().Be("linql");
        request.Document.Kind.Should().Be(OperationKind.Subscription);
        Encoding.UTF8.GetString(request.Document.Body).Should().Be("subscription linql { counter }");
    }

    [Fact]
    public async Task RejectsDeclaredSubscriptionsOnSendAsync()
    {
        var connection = new StubConnection("{\"data\":{\"counter\":1}}");
        var client = connection.WithLinQL();

        var send = () => client.SendAsync(DeclaredCounter());

        await send.Should().ThrowAsync<NotSupportedException>();
    }

    private static LinQLQuery<TestSubscription, int> DeclaredCounter() => LinQLQuery<TestSubscription, int>.Bind(
        "subscription linql { counter }",
        RootOperationType.Subscription,
        Counter);

    private sealed class CollectingObserver(ICollection<int> values, TaskCompletionSource completed) : IObserver<LinQLResult<int>>
    {
        public void OnCompleted() => completed.TrySetResult();

        public void OnError(Exception error) => completed.TrySetException(error);

        public void OnNext(LinQLResult<int> value) => values.Add(value.Data);
    }
}
