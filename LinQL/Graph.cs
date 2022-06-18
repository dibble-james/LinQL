namespace LinQL;

using LinQL.Translation;
using LinQL.Expressions;
using FastExpressionCompiler;

/// <summary>
/// The base class for defining GraphQL graphs.
/// </summary>
public abstract class Graph
{
    private readonly IGraphQLConnection connection;

    /// <summary>
    /// Create a new Graph.
    /// </summary>
    /// <param name="connection">How to query the server.</param>
    /// <param name="queryTranslator">Expression converter.</param>
    protected Graph(IGraphQLConnection connection, IQueryTranslator queryTranslator)
        => (this.connection, this.QueryTranslator) = (connection, queryTranslator);

    /// <summary>
    /// Gets the <see cref="IQueryTranslator"/> used by this graph.
    /// </summary>
    public IQueryTranslator QueryTranslator { get; }

    /// <summary>
    /// Run a <see cref="GraphQLExpression{TRoot, TResult}"/>.
    /// </summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <typeparam name="TData">The response type.</typeparam>
    /// <param name="query">The query to execute on the server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server data response.</returns>
    public async Task<TData> Execute<T, TData>(GraphQLExpression<T, TData> query, CancellationToken cancellationToken = default)
        => UnwrapResult(await this.FromRawGraphQLToResult<T>(this.QueryTranslator.ToQueryString(query), null, cancellationToken).ConfigureAwait(false), query);

    /// <summary>
    /// Run a <see cref="GraphQLExpression{TRoot, TResult}"/> and return the server response.
    /// </summary>
    /// <typeparam name="T">The root type.</typeparam>
    /// <typeparam name="TData">The response type.</typeparam>
    /// <param name="query">The query to execute on the server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    public async Task<GraphQLResponse<TData>> ExecuteToResult<T, TData>(GraphQLExpression<T, TData> query, CancellationToken cancellationToken = default)
    {
        var result = await this.FromRawGraphQLToResult<T>(this.QueryTranslator.ToQueryString(query), null, cancellationToken).ConfigureAwait(false);

        return new GraphQLResponse<TData>(UnwrapResult(result, query), result.Errors);
    }

    /// <summary>
    /// Send a manually created query to the server. Your query result must be deserializable to <typeparamref name="TData"/>.
    /// </summary>
    /// <typeparam name="TData">The type returned by the server.</typeparam>
    /// <param name="query">The raw GQL to send to the server.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The query result.</returns>
    public Task<TData> FromRawGraphQL<TData>(string query, CancellationToken cancellationToken = default)
        => this.FromRawGraphQL<TData>(query, null, cancellationToken);

    /// <summary>
    /// Send a manually created query to the server. Your query result must be deserializable to <typeparamref name="TData"/>.
    /// </summary>
    /// <typeparam name="TData">The type returned by the server.</typeparam>
    /// <param name="query">The raw GQL to send to the server.</param>
    /// <param name="variables">Any variables required by the query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The query result.</returns>
    public async Task<TData> FromRawGraphQL<TData>(string query, IReadOnlyDictionary<string, object>? variables = null, CancellationToken cancellationToken = default)
    {
        var response = await this.FromRawGraphQLToResult<TData>(query, variables, cancellationToken).ConfigureAwait(false);

        return response.Data;
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
        var response = await this.connection.SendRequest<TData>(new GraphQLRequest(this, query, variables), cancellationToken).ConfigureAwait(false);

        return response;
    }

    /// <summary>
    /// Build a <see cref="RootType{T}"/> for use on this graph.
    /// </summary>
    /// <typeparam name="T">The root type type.</typeparam>
    /// <returns>The root type.</returns>
    protected T RootType<T>()
        where T : RootType<T>, new() => new() { Graph = this };

    private static TData UnwrapResult<TRoot, TData>(GraphQLResponse<TRoot> response, GraphQLExpression<TRoot, TData> expression)
        => expression.OriginalQuery.CompileFast()(response.Data);
}
