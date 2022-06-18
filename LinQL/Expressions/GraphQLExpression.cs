namespace LinQL.Expressions;

using System.Linq.Expressions;
using LinQL.Description;

/// <summary>
/// An <see cref="Expression"/> representation of a GraphQL query.
/// </summary>
/// <typeparam name="TRoot">The root query type.</typeparam>
/// <typeparam name="TResult">The query result type.</typeparam>
public class GraphQLExpression<TRoot, TResult> : TypeFieldExpression
{
    private readonly Graph graph;

    /// <param name="graph">The graph to execute the query against.</param>
    /// <param name="rootOperation">The root operation to query against.</param>
    /// <param name="originalQuery">The expression this will be based on.</param>
    public GraphQLExpression(Graph graph, RootOperation rootOperation, Expression<Func<TRoot, TResult>> originalQuery)
        : base(rootOperation.Name, typeof(TRoot))
        => (this.graph, this.RootOperation, this.OriginalQuery) = (graph, rootOperation, originalQuery);

    /// <summary>
    /// Gets the root operation.
    /// </summary>
    public RootOperation RootOperation { get; }

    /// <summary>
    /// Gets the expressions this expression was built from.
    /// </summary>
    public Expression<Func<TRoot, TResult>> OriginalQuery { get; }

    /// <summary>
    /// Run the query against the graph.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server data response.</returns>
    public async Task<TResult> Execute(CancellationToken cancellationToken = default)
    {
        var result = await this.graph.Execute(this, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Execute the query and get the server response.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    public async Task<GraphQLResponse<TResult>> ToResult(CancellationToken cancellationToken = default)
    {
        var result = await this.graph.ExecuteToResult(this, cancellationToken).ConfigureAwait(false);

        return result;
    }
}
