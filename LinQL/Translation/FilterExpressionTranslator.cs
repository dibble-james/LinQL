namespace LinQL.Translation;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using LinQL.Description;
using LinQL.Expressions;
using LinQL.Filters;

/// <summary>
/// Translates a filter builder expression into a <see cref="FilterValue"/>.
/// </summary>
/// <remarks>
/// Only builder methods carrying <see cref="GraphQLFilterFieldAttribute"/> are understood; no C#
/// operator or method name is interpreted, so the filter can only express what the schema declares.
/// </remarks>
internal static class FilterExpressionTranslator
{
    public static FilterValue Translate(Expression expression)
    {
        if (expression.UnwrapLambdaFromQuote() is { } lambda)
        {
            var value = FilterValue.Create();
            Collect(lambda.Body, value);

            return value;
        }

        return FilterValue.From(Evaluate(expression));
    }

    private static void Collect(Expression? expression, FilterValue target)
    {
        if (expression is null or ParameterExpression)
        {
            return;
        }

        if (expression is not MethodCallExpression call)
        {
            throw new InvalidOperationException($"A filter can only contain filter builder calls, but found '{expression}'.");
        }

        Collect(call.Object, target);

        var field = call.Method.GetCustomAttribute<GraphQLFilterFieldAttribute>()
            ?? throw new InvalidOperationException($"'{call.Method.Name}' is not a filter builder operation.");

        var argument = call.Arguments.SingleOrDefault()
            ?? throw new InvalidOperationException($"'{call.Method.Name}' must take a single filter value.");

        if (argument.UnwrapLambdaFromQuote() is { } nested)
        {
            var nestedValue = FilterValue.Create();
            Collect(nested.Body, nestedValue);

            if (field.IsList)
            {
                target.Combine(field.Name, nestedValue);
            }
            else
            {
                target.Set(field.Name, nestedValue);
            }

            return;
        }

        target.Set(field.Name, Evaluate(argument));
    }

    private static object? Evaluate(Expression expression)
        => Expression.Lambda<Func<object>>(Expression.Convert(expression, typeof(object))).CompileFast()();
}
