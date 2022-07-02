namespace LinQL.Translation;

using System;
using System.Linq.Expressions;
using LinQL.Expressions;

/// <summary>
/// Default implementation of <see cref="IQueryTranslator"/>.
/// </summary>
public class TranslationProvider : IQueryTranslator
{
    /// <inheritdoc/>
    public GraphQLExpression<TRoot, TData> ToExpression<TRoot, TData>(Graph graph, Expression<Func<TRoot, TData>> query)
        => ExpressionTranslator.Translate(graph, query);

    /// <inheritdoc/>
    public GraphQLExpression<TRoot, TData> Include<TRoot, TData>(GraphQLExpression<TRoot, TData> expression, Expression<Func<TRoot, object>> include)
        => ExpressionTranslator.Include(expression, include);

    /// <inheritdoc/>
    public string ToQueryString<TRoot, TData>(GraphQLExpression<TRoot, TData> query)
    {
        var expressionTranslator = new GraphQLExpressionTranslator<TRoot, TData>();

        return expressionTranslator.Translate(query);
    }
}
