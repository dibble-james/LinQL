namespace LinQL.StrawberryShake.Tests;

using System.Text.Json;
using FluentAssertions;
using global::StrawberryShake;
using Xunit;

public class CachingTests
{
    [Fact]
    public async Task CacheFirstReplaysCachedResultWithoutHittingTheConnection()
    {
        var connection = new SequentialConnection("{\"data\":{\"number\":7}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheFirst);

        var first = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
            first.Values.Should().Equal(7);
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(1);
            second.Values.Should().Equal(7);
        }

        connection.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task CacheAndNetworkEmitsCachedResultThenRefreshesIt()
    {
        var connection = new SequentialConnection(
            "{\"data\":{\"number\":1}}",
            "{\"data\":{\"number\":2}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheAndNetwork);

        var first = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(2);
            second.Values.Should().Equal(1, 2);
        }

        connection.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task NetworkOnlyNeverReplaysCachedResults()
    {
        var connection = new SequentialConnection(
            "{\"data\":{\"number\":1}}",
            "{\"data\":{\"number\":2}}");
        using var client = connection.WithLinQL();

        var first = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
            first.Values.Should().Equal(1);
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(1);
            second.Values.Should().Equal(2);
        }

        connection.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task VariablesParticipateInTheCacheKey()
    {
        var connection = new SequentialConnection(
            "{\"data\":{\"film\":{\"title\":\"A New Hope\",\"episode\":4}}}",
            "{\"data\":{\"film\":{\"title\":\"The Empire Strikes Back\",\"episode\":5}}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheFirst);

        var first = new CollectingObserver<TestFilm>();
        using (client.Watch((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
            first.Values.Should().Equal(new TestFilm("A New Hope", 4));
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<TestFilm>();
        using (client.Watch((TestQuery q) => q.GetFilm("2"), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(1);
            second.Values.Should().Equal(new TestFilm("The Empire Strikes Back", 5));
        }

        connection.Requests.Should().HaveCount(2);

        var replay = new CollectingObserver<TestFilm>();
        using (client.Watch((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken).Subscribe(replay))
        {
            await replay.WaitForAsync(1);
            replay.Values.Should().Equal(new TestFilm("A New Hope", 4));
        }

        connection.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CacheAndNetworkDoesNotEmitUnchangedData()
    {
        var connection = new SequentialConnection(
            "{\"data\":{\"number\":1}}",
            "{\"data\":{\"number\":1}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheAndNetwork);

        var first = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(1);
            await connection.WaitForCompletedRequestsAsync(2);
            second.Values.Should().Equal(1);
        }

        connection.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task CacheAndNetworkEmitsWhenErrorsChange()
    {
        var connection = new SequentialConnection(
            "{\"data\":{\"number\":1},\"errors\":[{\"message\":\"first\"}]}",
            "{\"data\":{\"number\":1},\"errors\":[{\"message\":\"second\"}]}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheAndNetwork);

        var first = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(first))
        {
            await first.WaitForAsync(1);
        }

        await WaitUntilAsync(() => client.OperationStore.GetAll().Any());

        var second = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(second))
        {
            await second.WaitForAsync(2);
            second.Values.Should().Equal(1, 1);
        }

        connection.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task SendAsyncPopulatesTheCacheForCacheFirst()
    {
        var connection = new SequentialConnection("{\"data\":{\"number\":3}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheFirst);

        var sent = await client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);
        sent.Data.Should().Be(3);

        var replay = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(replay))
        {
            await replay.WaitForAsync(1);
            replay.Values.Should().Equal(3);
        }

        connection.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task DisposingTheClientStopsActiveWatches()
    {
        var connection = new GatedConnection();
        using var client = connection.WithLinQL();
        var values = new CollectingObserver<int>();

        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(values))
        {
            await WaitUntilAsync(() => connection.Requests.Count == 1);

            client.Dispose();
            connection.Release("{\"data\":{\"number\":1}}");

            await connection.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            values.Values.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CancellingTheWatchTokenStopsTheWatch()
    {
        var connection = new GatedConnection();
        using var client = connection.WithLinQL();
        using var cancellation = new CancellationTokenSource();
        var values = new CollectingObserver<int>();

        using (client.Watch(
            (TestQuery q) => q.GetNumber(),
            cancellationToken: cancellation.Token).Subscribe(values))
        {
            await WaitUntilAsync(() => connection.Requests.Count == 1);

            cancellation.Cancel();
            connection.Release("{\"data\":{\"number\":1}}");

            await connection.Finished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            values.Values.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task DisposingTheClientCompletesCachedWatches()
    {
        var connection = new SequentialConnection("{\"data\":{\"number\":5}}");
        using var client = connection.WithLinQL(options => options.Strategy = ExecutionStrategy.CacheFirst);

        await client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        var values = new CollectingObserver<int>();
        using (client.Watch((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken).Subscribe(values))
        {
            await values.WaitForAsync(1);

            client.Dispose();

            await values.WaitForCompletionAsync();
            values.IsCompleted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DisposingTheClientDisposesTheStores()
    {
        var connection = new SequentialConnection("{\"data\":{\"number\":1}}");
        var client = connection.WithLinQL();

        client.OperationStore.Should().NotBeNull();

        client.Dispose();
        client.Dispose();

        client.Invoking(c => c.OperationStore.Clear()).Should().Throw<ObjectDisposedException>();
        client.Invoking(c => c.Watch((TestQuery q) => q.GetNumber())).Should().Throw<ObjectDisposedException>();

        Func<Task> send = () => client.SendAsync((TestQuery q) => q.GetNumber());
        await send.Should().ThrowAsync<ObjectDisposedException>();
    }

    private sealed class SequentialConnection : IConnection<JsonDocument>
    {
        private readonly Queue<string> responses;

        public SequentialConnection(params string[] json)
            => this.responses = new Queue<string>(json);

        private int completedRequests;

        public List<OperationRequest> Requests { get; } = [];

        public async Task WaitForCompletedRequestsAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (Volatile.Read(ref this.completedRequests) < count)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"Expected {count} completed request(s) but observed {this.completedRequests}.");
                }

                await Task.Delay(10);
            }
        }

        public async IAsyncEnumerable<Response<JsonDocument>> ExecuteAsync(OperationRequest request)
        {
            this.Requests.Add(request);
            await Task.Yield();

            try
            {
                if (this.responses.Count == 0)
                {
                    throw new InvalidOperationException("No response was queued for this request.");
                }

                yield return new Response<JsonDocument>(
                    JsonDocument.Parse(this.responses.Dequeue()),
                    null);
            }
            finally
            {
                Interlocked.Increment(ref this.completedRequests);
            }
        }
    }

    private sealed class GatedConnection : IConnection<JsonDocument>
    {
        private readonly TaskCompletionSource<string> response = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<OperationRequest> Requests { get; } = [];

        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(string json) => this.response.TrySetResult(json);

        public async IAsyncEnumerable<Response<JsonDocument>> ExecuteAsync(OperationRequest request)
        {
            this.Requests.Add(request);

            try
            {
                var json = await this.response.Task;

                yield return new Response<JsonDocument>(JsonDocument.Parse(json), null);
            }
            finally
            {
                this.Finished.TrySetResult();
            }
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected condition was not met.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class CollectingObserver<T> : IObserver<LinQLResult<T>>
    {
        private readonly object sync = new();
        private readonly List<T> values = [];
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public T[] Values
        {
            get
            {
                lock (this.sync)
                {
                    return this.values.ToArray();
                }
            }
        }

        public Exception? Error { get; private set; }

        public bool IsCompleted => this.completed.Task.IsCompleted;

        public void OnCompleted() => this.completed.TrySetResult();

        public Task WaitForCompletionAsync() => this.completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void OnError(Exception error) => this.Error = error;

        public void OnNext(LinQLResult<T> value)
        {
            lock (this.sync)
            {
                this.values.Add(value.Data);
            }
        }

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (this.Values.Length < count)
            {
                if (this.Error is { } error)
                {
                    throw error;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"Expected {count} result(s) but received {this.Values.Length}.");
                }

                await Task.Delay(10);
            }
        }
    }
}
