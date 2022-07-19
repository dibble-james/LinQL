namespace LinQL;

using LinQL.Translation;
using LinQL.Expressions;
using FastExpressionCompiler;
using Microsoft.Extensions.Logging;

/// <summary>
/// The base class for defining GraphQL graphs.
/// </summary>
public abstract class Graph
{
    private static readonly Action<ILogger, string, Exception?> SendingGraphQLRequest = LoggerMessage.Define<string>(
        LogLevel.Debug,
        new EventId(1, nameof(SendingGraphQLRequest)),
        "Sending GraphQL query: {Query}");
    private static readonly Action<ILogger, string[], Exception?> FailedGraphQLResponse = LoggerMessage.Define<string[]>(
        LogLevel.Error,
        new EventId(2, nameof(FailedGraphQLResponse)),
        "GrapQL response contains errors: {Errors}");

    private readonly ILogger<Graph> logger;
    private readonly GraphOptions options;

    /// <summary>
    /// Create a new Graph.
    /// </summary>
    /// <param name="logger">Access to a logger.</param>
    /// <param name="options">The graph configuration.</param>
    /// <param name="queryTranslator">Expression converter.</param>
    protected Graph(ILogger<Graph> logger, GraphOptions options, IQueryTranslator queryTranslator)
        => (this.logger, this.options, this.QueryTranslator) = (logger, options, queryTranslator);

    /// <summary>
    /// Gets the <see cref="IQueryTranslator"/> used by this graph.
    /// </summary>
    public virtual IQueryTranslator QueryTranslator { get; }

    /// <summary>
    /// Run a <see cref="GraphQLExpression{TRoot, TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <typeparam name="TData">The response type.</typeparam>
    /// <param name="query">The query to execute on the server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server data response.</returns>
    public async Task<TData?> Execute<T, TData>(GraphQLExpression<T?, TData> query, CancellationToken cancellationToken = default)
    {
        var result = await this.FromRawGraphQL<T>(this.QueryTranslator.ToQueryString(query), null, cancellationToken).ConfigureAwait(false);

        return UnwrapResult(result, query);
    }

    /// <summary>
    /// Run a <see cref="GraphQLExpression{TRoot, TResult}"/> and return the server response.
    /// </summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <typeparam name="TData">The response type.</typeparam>
    /// <param name="query">The query to execute on the server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    public async Task<GraphQLResponse<TData>> ExecuteToResult<T, TData>(GraphQLExpression<T?, TData> query, CancellationToken cancellationToken = default)
    {
        var result = await this.FromRawGraphQLToResult<T>(this.QueryTranslator.ToQueryString(query), null, cancellationToken).ConfigureAwait(false);

        return new GraphQLResponse<TData>(UnwrapResult(result.Data, query), result.Errors) { Request = result.Request };
    }

    /// <summary>
    /// Send a manually created query to the server. Your query result must be deserializable to <typeparamref name="TData"/>.
    /// </summary>
    /// <typeparam name="TData">The type returned by the server.</typeparam>
    /// <param name="query">The raw GQL to send to the server.</param>
    /// <param name="variables">Any variables required by the query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The query result.</returns>
    public async Task<TData?> FromRawGraphQL<TData>(string query, IReadOnlyDictionary<string, object>? variables = null, CancellationToken cancellationToken = default)
    {
        var response = await this.FromRawGraphQLToResult<TData?>(query, variables, cancellationToken).ConfigureAwait(false);

        return response.EnsureSuccessfulResponse();
    }

    /// <summary>
    /// Send a manually created query to the server. Your query result must be deserializable to <typeparamref name="TData"/>.
    /// </summary>
    /// <typeparam name="TData">The type returned by the server.</typeparam>
    /// <param name="query">The raw GQL to send to the server.</param>
    /// <param name="variables">Any variables required by the query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The query result.</returns>
    public async Task<GraphQLResponse<TData>> FromRawGraphQLToResult<TData>(string query, IReadOnlyDictionary<string, object>? variables = null, CancellationToken cancellationToken = default)
    {
        var connection = this.options.Connection ?? throw new InvalidOperationException("The graph has not been configured with a subscription connection.");

        SendingGraphQLRequest(this.logger, query, null);

        var response = await this.options.Connection().SendRequest<TData>(new GraphQLRequest(query, variables), cancellationToken).ConfigureAwait(false);

        if (response.Errors?.Any() == true)
        {
            FailedGraphQLResponse(this.logger, response.Errors.Select(x => x.ToString()).ToArray(), null);
        }

        return response;
    }

    /// <summary>
    /// Start a subscription connection.
    /// </summary>
    /// <typeparam name="TRoot">The root query type.</typeparam>
    /// <typeparam name="TData">The expected result.</typeparam>
    /// <param name="query">The query to subscribe too.</param>
    /// <param name="handler">The subscriber.</param>
    /// <param name="onComplete">A callback for when the subscription ends.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A handle on the subscription.</returns>
    public async Task<IDisposable> Subscribe<TRoot, TData>(GraphQLExpression<TRoot?, TData> query, OnSubscriptionMessage<TData> handler, Func<CancellationToken, Task>? onComplete = default, CancellationToken cancellationToken = default)
    {
        var request = this.TranslateSubscriptionRequest(query);

        return await this.options.GetSubscriptionConnection().Subscribe(
            request,
            HandleSubscription(query, request, handler),
            onComplete,
            cancellationToken);
    }

    /// <summary>
    /// Start a subscription connection.
    /// </summary>
    /// <typeparam name="TRoot">The root query type.</typeparam>
    /// <typeparam name="TData">The expected result.</typeparam>
    /// <param name="query">The query to subscribe too.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A handle on the subscription.</returns>
    public IAsyncEnumerable<GraphQLResponse<TData?>> Subscribe<TRoot, TData>(GraphQLExpression<TRoot?, TData> query, CancellationToken cancellationToken = default)
    {
        var request = this.TranslateSubscriptionRequest(query);

        return this.options.GetSubscriptionConnection().Subscribe<TData>(
            request,
            cancellationToken);
    }

    /// <summary>
    /// Build a <see cref="RootType{T}"/> for use on this graph.
    /// </summary>
    /// <typeparam name="T">The root type type.</typeparam>
    /// <returns>The root type.</returns>
    protected T RootType<T>()
        where T : RootType<T>, new() => new() { Graph = this };

    private static TData? UnwrapResult<TRoot, TData>(TRoot response, GraphQLExpression<TRoot, TData> expression)
        => response is null ? default : expression.OriginalQuery.CompileFast()(response);

    private static OnSubscriptionMessage<TRoot> HandleSubscription<TRoot, TData>(GraphQLExpression<TRoot?, TData> query, GraphQLRequest request, OnSubscriptionMessage<TData> handler) =>
        (GraphQLResponse<TRoot?> response, CancellationToken ct) =>
        {
            var unwrappedResponse = new GraphQLResponse<TData?>(UnwrapResult(response.Data, query), response.Errors)
            {
                Request = request,
            };

            return handler(unwrappedResponse, ct);
        };

    private GraphQLRequest TranslateSubscriptionRequest<TRoot, TData>(GraphQLExpression<TRoot, TData> query)
    {
        if (query.RootOperation != Description.RootOperation.Subscription)
        {
            throw new InvalidOperationException("You can only subscribe to subscriptions");
        }

        var request = new GraphQLRequest(this.QueryTranslator.ToQueryString(query));
        return request;
    }
}
