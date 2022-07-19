namespace LinQL.Translation;

using System;
using System.Linq.Expressions;

/// <summary>
/// 
/// </summary>
public static class TranslationProvider
{
    public static GraphQLExpressionRequest<TRoot, TData> ToGraphQLRequest<TRoot, TData>(this Expression<Func<TRoot, TData>> query, Graph graph)
    {
        var expression = ExpressionTranslator.Translate(query);

        var request = GraphQLExpressionTranslator.Translate(expression, graph);

        return request;
    }
}
