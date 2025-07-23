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

    /// <summary>
    /// Instruct the query to get all scalar fields on this type.
    /// </summary>
    /// <typeparam name="T">The type to select from.</typeparam>
    /// <param name="that">The type.</param>
    /// <returns>The type.</returns>
    public static T SelectAll<T>(this T that) => that;

    /// <summary>
    /// Instruct the query to get all scalar fields on this type.
    /// </summary>
    /// <typeparam name="T">The type to select from.</typeparam>
    /// <typeparam name="TResult">The type to project to.</typeparam>
    /// <param name="that">The type.</param>
    /// <param name="projection">The expression required to create <typeparamref name="TResult"/> from <typeparamref name="T"/>.</param>
    /// <returns>The projection.</returns>
    public static TResult Project<T, TResult>(this T that, Expression<Func<T, TResult>> projection) => projection.CompileFast()(that);
}
