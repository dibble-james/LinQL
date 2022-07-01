namespace LinQL;

using System.Linq.Expressions;
using FastExpressionCompiler;
using OneOf;

/// <summary>
/// Helpers for projecting to GraphQL queries.
/// </summary>
public static class SelectExtentions
{
    /// <summary>
    /// Spread a given type.
    /// </summary>
    /// <typeparam name="T">The interface or union</typeparam>
    /// <typeparam name="TMaybe">The type to spread as</typeparam>
    /// <typeparam name="TResult">The result</typeparam>
    /// <param name="that">The interface or union</param>
    /// <param name="spread">The expression to run over <typeparamref name="TMaybe"/>.</param>
    /// <returns>The result or the original object if it isn't <typeparamref name="TMaybe"/></returns>
    public static OneOf<T, TResult> On<T, TMaybe, TResult>(this T that, Expression<Func<TMaybe, TResult>> spread)
        where TMaybe : T
    {
        if (that is TMaybe target)
        {
            return spread.CompileFast()(target);
        }

        return that;
    }

    /// <summary>
    /// Spread a given type.
    /// </summary>
    /// <typeparam name="T">The interface or union</typeparam>
    /// <typeparam name="TMaybe">The type to spread as</typeparam>
    /// <typeparam name="TResult">The result</typeparam>
    /// <param name="that">The interface or union</param>
    /// <param name="spread">The expression to run over <typeparamref name="TMaybe"/>.</param>
    /// <returns>The result or the original object if it isn't <typeparamref name="TMaybe"/></returns>
    public static OneOf<T, TResult> On<T, TMaybe, TResult>(this OneOf<T, TResult> that, Expression<Func<TMaybe, TResult>> spread)
    {
        if (that.IsT1)
        {
            return that.AsT1;
        }

        if (that.AsT0 is TMaybe target)
        {
            return spread.CompileFast()(target);
        }

        return that.AsT0;
    }
}
