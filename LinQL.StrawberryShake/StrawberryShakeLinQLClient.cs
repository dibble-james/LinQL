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
public sealed class StrawberryShakeLinQLClient
{
    private readonly IConnection<JsonDocument> connection;
    private readonly LinQLStrawberryShakeOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrawberryShakeLinQLClient"/> class.
    /// </summary>
    /// <param name="connection">The Strawberry Shake connection used to send requests.</param>
    /// <param name="options">The client options.</param>
    public StrawberryShakeLinQLClient(IConnection<JsonDocument> connection, LinQLStrawberryShakeOptions? options = null)
    {
        this.connection = connection ?? throw new ArgumentNullException(nameof(connection));
        this.options = options ?? new LinQLStrawberryShakeOptions();
    }

    /// <summary>
    /// Gets the LinQL configuration used to translate expressions.
    /// </summary>
    public LinQLOptions Options => this.options.LinQL;

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
    /// <returns>The result stream.</returns>
    public IObservable<LinQLResult<TData>> Watch<TRoot, TData>(
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null)
        where TRoot : RootType<TRoot>
    {
        var translated = request.ToRequest(this.options.LinQL, includes);
        var expression = translated.Expression;
        var operation = LinQLOperationRequest.Create(expression.RootOperation.Type, translated.Query, expression.Variables);
        var executor = this.CreateExecutor(expression, translated.Query);

        var results = executor.Watch(operation);

        return new MappedObservable<IOperationResult<LinQLResult<TData>>, LinQLResult<TData>>(
            results,
            static result => result.Data ?? throw new InvalidOperationException("The LinQL result builder did not produce a result."));
    }

    private StorelessOperationExecutor<JsonDocument, LinQLResult<TData>> CreateExecutor<TRoot, TData>(
        GraphQLExpression<TRoot, TData> expression,
        string query)
        where TRoot : RootType<TRoot>
    {
        var projection = expression.OriginalQuery.CompileFast();

        return new(
            this.connection,
            () => new LinQLResultBuilder<TRoot, TData>(projection, this.options.Serializer, query),
            static () => new JsonResultPatcher());
    }
}
