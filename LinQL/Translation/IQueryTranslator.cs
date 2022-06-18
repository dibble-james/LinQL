namespace LinQL.Translation;

using System.Linq.Expressions;
using LinQL.Expressions;

/// <summary>
/// Converts <see cref="Expression"/>s into GraphQL queries.
/// </summary>
public interface IQueryTranslator
{
    /// <summary>
    /// Translate a .Net expression into a <see cref="GraphQLExpression{TRoot, TResult}"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root operation to start the query from.</typeparam>
    /// <typeparam name="TData">The result of the query.</typeparam>
    /// <param name="graph">The parent graph.</param>
    /// <param name="query">The query to translate.</param>
    /// <returns>The translated expression.</returns>
    GraphQLExpression<TRoot, TData> ToExpression<TRoot, TData>(Graph graph, Expression<Func<TRoot, TData>> query);

    /// <summary>
    /// Convert a <see cref="GraphQLExpression{TRoot, TResult}"/> to a GraphQL query string.
    /// </summary>
    /// <typeparam name="TRoot">The root operation to start the query from.</typeparam>
    /// <typeparam name="TData">The result of the query.</typeparam>
    /// <param name="query">The query to translate.</param>
    /// <returns>The translated query string.</returns>
    string ToQueryString<TRoot, TData>(GraphQLExpression<TRoot, TData> query);
}
