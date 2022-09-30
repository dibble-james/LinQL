namespace LinQL.GraphQL.Client;

using global::GraphQL;
using LinQL.Expressions;

/// <summary>
/// Helpers for <see cref="GraphQLExpression{TRoot, TData}"/>.
/// </summary>
public static class GraphQLExpressionExtensions
{
    /// <summary>
    /// Convert to a <see cref="GraphQLRequest"/>.
    /// </summary>
    /// <returns>The request.</returns>
    public static GraphQLExpressionRequest<TRoot, TData> ToGraphQLClientRequest<TRoot, TData>(this LinqQLRequest<TRoot, TData> request)
        => new(request.Expression) { Query = request.Query };
}
