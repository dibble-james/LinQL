namespace LinQL.StrawberryShake.Tests;

using System.Text;
using FluentAssertions;
using global::StrawberryShake;
using Xunit;

public class QueryTests
{
    [Fact]
    public async Task SendScalarQuery()
    {
        var connection = new StubConnection("{\"data\":{\"number\":12345}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetNumber());

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(12345);
        result.Query.Should().StartWith("query linql {");
    }

    [Fact]
    public async Task SendObjectQuery()
    {
        var connection = new StubConnection("{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetFilm("1"));

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestFilm("A New Hope", 4));
    }

    [Fact]
    public async Task SendMutation()
    {
        var connection = new StubConnection("{\"data\":{\"setNumber\":{\"number\":54321}}}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestMutation m) => m.SetNumberOperation(54321));

        result.Errors.Should().BeEmpty();
        result.Data.Number.Should().Be(54321);
    }

    [Fact]
    public async Task SendsVariablesAndNamedOperation()
    {
        var connection = new StubConnection("{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}");
        var client = connection.WithLinQL();

        await client.SendAsync((TestQuery q) => q.GetFilm("1"));

        var request = connection.Requests.Should().ContainSingle().Subject;

        request.Name.Should().Be("linql");
        request.Document.Kind.Should().Be(OperationKind.Query);
        Encoding.UTF8.GetString(request.Document.Body).Should().StartWith("query linql(");
        request.Variables["var1"].Should().Be("1");
    }

    [Fact]
    public async Task MapsErrors()
    {
        var connection = new StubConnection(
            "{\"data\":null,\"errors\":[{\"message\":\"boom\",\"code\":\"E1\",\"path\":[\"film\"],\"locations\":[{\"line\":2,\"column\":3}]}]}");
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetFilm("1"));

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

        var result = await client.SendAsync((TestQuery q) => q.GetNumber());

        result.Extensions.Should().ContainKey("traceId");
    }

    [Fact]
    public async Task MapsTransportExceptions()
    {
        var connection = new StubConnection(new HttpRequestException("down"));
        var client = connection.WithLinQL();

        var result = await client.SendAsync((TestQuery q) => q.GetNumber());

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
            .Watch((TestSubscription s) => s.GetCounter())
            .Subscribe(new CollectingObserver(values, completed));

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        values.Should().Equal(1, 2);
        connection.Requests.Should().ContainSingle().Which.Document.Kind.Should().Be(OperationKind.Subscription);
    }

    private sealed class CollectingObserver(ICollection<int> values, TaskCompletionSource completed) : IObserver<LinQLResult<int>>
    {
        public void OnCompleted() => completed.TrySetResult();

        public void OnError(Exception error) => completed.TrySetException(error);

        public void OnNext(LinQLResult<int> value) => values.Add(value.Data);
    }
}
