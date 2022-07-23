namespace LinQL.Translation;

using System;
using System.Linq.Expressions;

/// <summary>
/// Convert an <see cref="Expression"/> to a <see cref="GraphQLRequest"/>.
/// </summary>
public static class TranslationProvider
{
    /// <summary>   
    /// Convert an <see cref="Expression"/> to a <see cref="GraphQLRequest"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root data type.</typeparam>
    /// <typeparam name="TData">The requested data type.</typeparam>
    /// <param name="query">The expression to be sent to the server.</param>
    /// <returns>The request to execute.</returns>
    public static GraphQLExpressionRequest<TRoot, TData> ToGraphQLRequest<TRoot, TData>(this Expression<Func<TRoot, TData>> query)
    {
        var expression = ExpressionTranslator.Translate(query);

        var request = GraphQLExpressionTranslator.Translate(expression);

        return request;
    }
}
