namespace LinQL.GraphQL.Client;

using global::GraphQL;
using LinQL.Expressions;

/// <summary>
/// A <see cref="GraphQLRequest"/> that was created from an Expression.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <typeparam name="TData">The requested data type.</typeparam>
/// <remarks>
/// Creates a request to be sent to a GraphQL server.
/// </remarks>
/// <param name="expression">The expression to send.</param>
public class GraphQLExpressionRequest<TRoot, TData>(GraphQLExpression<TRoot, TData> expression) : GraphQLRequest
    where TRoot : RootType<TRoot>
{

    /// <summary>
    /// Gets the expression to send to the server.
    /// </summary>
    public GraphQLExpression<TRoot, TData> Expression { get; } = expression;
}
