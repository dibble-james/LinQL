namespace LinQL;

using LinQL.Expressions;

/// <summary>
/// Defines the result of translation.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <typeparam name="TData">The result type.</typeparam>
/// <param name="Expression">The translated expression.</param>
/// <param name="Query">The GQL to send to the server.</param>
/// <param name="Variables">The values used in the query.</param>
public record LinqQLRequest<TRoot, TData>(GraphQLExpression<TRoot, TData> Expression, string Query, object? Variables)
    where TRoot : RootType<TRoot>;
