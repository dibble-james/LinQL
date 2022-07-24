namespace LinQL.Translation;

using System;
using System.Linq.Expressions;
using LinQL.Expressions;

/// <summary>
/// Convert an <see cref="Expression"/> to a <see cref="LinqQLRequest{TRoot, TData}"/>.
/// </summary>
public static class TranslationProvider
{
    /// <summary>   
    /// Convert an <see cref="Expression"/> to a <see cref="LinqQLRequest{TRoot, TData}"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root data type.</typeparam>
    /// <typeparam name="TData">The requested data type.</typeparam>
    /// <param name="query">The expression to be sent to the server.</param>
    /// <param name="includes">Any extra fields required.</param>
    /// <returns>The request to execute.</returns>
    public static LinqQLRequest<TRoot, TData> ToRequest<TRoot, TData>(
        this Expression<Func<TRoot, TData>> query,
        Action<GraphQLExpression<TRoot, TData>>? includes = null)
    {
        var expression = ExpressionTranslator.Translate(query);
        includes?.Invoke(expression);

        var request = GraphQLExpressionTranslator.Translate(expression);

        return request;
    }
}
