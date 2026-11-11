namespace LinQL.StrawberryShake;

using System.Linq.Expressions;
using System.Text.Json;
using FastExpressionCompiler;
using LinQL.Description;
using LinQL.Expressions;
using LinQL.Translation;
using global::StrawberryShake;
using global::StrawberryShake.Json;

/// <summary>
/// Executes LinQL expressions over a Strawberry Shake <see cref="IConnection{TResponseBody}"/>.
/// </summary>
public sealed class StrawberryShakeLinQLClient : IDisposable
{
    private readonly IConnection<JsonDocument> connection;
    private readonly LinQLStrawberryShakeOptions options;
    private readonly EntityStore entityStore;
    private readonly OperationStore operationStore;
    private readonly object subscriptionsSync = new();
    private readonly HashSet<TrackedSubscription> subscriptions = [];
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrawberryShakeLinQLClient"/> class.
    /// </summary>
    /// <param name="connection">The Strawberry Shake connection used to send requests.</param>
    /// <param name="options">The client options.</param>
    public StrawberryShakeLinQLClient(IConnection<JsonDocument> connection, LinQLStrawberryShakeOptions? options = null)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.options = options ?? new LinQLStrawberryShakeOptions();
        this.entityStore = new EntityStore();
        this.operationStore = new OperationStore(this.entityStore);
    }

    /// <summary>
    /// Gets the LinQL configuration used to translate expressions.
    /// </summary>
    public LinQLOptions Options => this.options.LinQL;

    /// <summary>
    /// Gets the operation store owned by this client.
    /// </summary>
    /// <remarks>
    /// The store caches finished results by operation (document and variables). It is disposed together with the
    /// client and is never shared with another client.
    /// </remarks>
    public IOperationStore OperationStore => this.operationStore;

    /// <summary>
    /// Executes a query or mutation and returns the projected result.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type.</typeparam>
    /// <typeparam name="TData">The projected result type.</typeparam>
    /// <param name="request">The expression to execute.</param>
    /// <param name="includes">Any extra fields required.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The projected result.</returns>
    public async Task<LinQLResult<TData>> SendAsync<TRoot, TData>(
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        var translated = request.ToRequest(this.options.LinQL, includes);
        var expression = translated.Expression;

        if (expression.RootOperation.Type is RootOperationType.Subscription)
        {
            throw new NotSupportedException("Subscriptions must be consumed through Watch.");
        }

        var operation = LinQLOperationRequest.Create(expression.RootOperation.Type, translated.Query, expression.Variables);
        var executor = this.CreateExecutor(expression, translated.Query);

        var result = await executor
            .ExecuteAsync(operation, cancellationToken)
            .ConfigureAwait(false);

        return result.Data ?? throw new InvalidOperationException("The LinQL result builder did not produce a result.");
    }

    /// <summary>
    /// Watches an operation, emitting a result for every message the server sends.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type.</typeparam>
    /// <typeparam name="TData">The projected result type.</typeparam>
    /// <param name="request">The expression to execute.</param>
    /// <param name="includes">Any extra fields required.</param>
    /// <param name="cancellationToken">A cancellation token that stops the watch.</param>
    /// <returns>The result stream.</returns>
    public IObservable<LinQLResult<TData>> Watch<TRoot, TData>(
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        var translated = request.ToRequest(this.options.LinQL, includes);
        var expression = translated.Expression;
        var operation = LinQLOperationRequest.Create(expression.RootOperation.Type, translated.Query, expression.Variables);
        var executor = this.CreateExecutor(expression, translated.Query);

        var results = executor.Watch(operation);
        var mapped = new MappedObservable<IOperationResult<LinQLResult<TData>>, LinQLResult<TData>>(
            results,
            static result => result.Data ?? throw new InvalidOperationException("The LinQL result builder did not produce a result."));

        return new TrackedObservable<LinQLResult<TData>>(
            mapped,
            subscription => this.TrackSourceSubscription(subscription, cancellationToken));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        TrackedSubscription[] active;

        lock (this.subscriptionsSync)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            active = this.subscriptions.ToArray();
            this.subscriptions.Clear();
        }

        foreach (var subscription in active)
        {
            subscription.Dispose();
        }

        this.operationStore.Dispose();
        this.entityStore.Dispose();
    }

    private TrackedSubscription TrackSourceSubscription(
        IDisposable subscription,
        CancellationToken cancellationToken)
    {
        var tracked = new TrackedSubscription(this, subscription, cancellationToken);

        lock (this.subscriptionsSync)
        {
            if (this.disposed)
            {
                tracked.Dispose();
                throw new ObjectDisposedException(nameof(StrawberryShakeLinQLClient));
            }

            this.subscriptions.Add(tracked);

            if (tracked.IsDisposed)
            {
                this.subscriptions.Remove(tracked);
            }
        }

        return tracked;
    }

    private void UntrackSubscription(TrackedSubscription subscription)
    {
        lock (this.subscriptionsSync)
        {
            this.subscriptions.Remove(subscription);
        }
    }

    private OperationExecutor<JsonDocument, LinQLResult<TData>> CreateExecutor<TRoot, TData>(
        GraphQLExpression<TRoot, TData> expression,
        string query)
        where TRoot : RootType<TRoot>
    {
        var projection = expression.OriginalQuery.CompileFast();

        return new(
            this.connection,
            () => new LinQLResultBuilder<TRoot, TData>(projection, this.options.Serializer, query),
            static () => new JsonResultPatcher(),
            this.operationStore,
            this.options.Strategy);
    }

    private sealed class TrackedObservable<T> : IObservable<T>
    {
        private readonly IObservable<T> source;
        private readonly Func<IDisposable, IDisposable> track;

        public TrackedObservable(IObservable<T> source, Func<IDisposable, IDisposable> track)
        {
            this.source = source;
            this.track = track;
        }

        public IDisposable Subscribe(IObserver<T> observer) => this.track(this.source.Subscribe(observer));
    }

    private sealed class TrackedSubscription : IDisposable
    {
        private readonly StrawberryShakeLinQLClient client;
        private readonly IDisposable inner;
        private readonly CancellationTokenRegistration registration;
        private int disposed;

        public TrackedSubscription(
            StrawberryShakeLinQLClient client,
            IDisposable inner,
            CancellationToken cancellationToken)
        {
            this.client = client;
            this.inner = inner;

            if (cancellationToken.CanBeCanceled)
            {
                this.registration = cancellationToken.Register(
                    static state => ((TrackedSubscription)state!).Cancel(),
                    this);
            }
        }

        public bool IsDisposed => Volatile.Read(ref this.disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            this.registration.Dispose();
            this.client.UntrackSubscription(this);
            this.inner.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            {
                return;
            }

            // The registration belongs to the caller's token, so it must not be disposed from its own callback.
            this.client.UntrackSubscription(this);
            this.inner.Dispose();
        }
    }
}
