namespace LinQL;

using LinQL.Expressions;

/// <summary>
/// A <see cref="GraphQLResponse{TData}"/> that was created from an Expression.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <typeparam name="TData">The requested data type.</typeparam>
public class GraphQLExpressionResponse<TRoot, TData> : GraphQLResponse<TData>
{
    /// <summary>
    /// Creates a request to be sent to a GraphQL server.
    /// </summary>
    /// <param name="expression">The expression to send.</param>
    public GraphQLExpressionResponse(GraphQLExpression<TRoot, TData> expression)
        => this.Expression = expression;

    /// <summary>
    /// Gets the expression to send to the server.
    /// </summary>
    public GraphQLExpression<TRoot, TData> Expression { get; }
}
