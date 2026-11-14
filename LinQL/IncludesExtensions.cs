namespace LinQL;

using System;
using System.Linq.Expressions;
using LinQL.Description;

/// <summary>
/// Adds fields to the selection a query projects.
/// </summary>
/// <remarks>
/// Both methods are translation-only: they return their receiver and have no runtime effect. The
/// translator resolves the field the method is called on and adds the fields the include lambda selects
/// to it, so an include attaches to the selection it is called on rather than to the document root.
/// </remarks>
public static class IncludesExtensions
{
    /// <summary>
    /// Adds the fields the include lambda selects to the field this is called on.
    /// </summary>
    /// <typeparam name="T">The type of the field the include is attached to.</typeparam>
    /// <param name="source">The field.</param>
    /// <param name="include">The fields to add.</param>
    /// <returns>The receiver, so the query continues from it.</returns>
    /// <remarks>
    /// The lambda is read from the expression tree by the translator; it is never compiled here, but a
    /// null one cannot be translated.
    /// </remarks>
    [GraphQLIncludes]
    public static T Includes<T>(this T source, Expression<Func<T, object>> include)
        where T : ICanHaveIncludes
        => include is null
            ? throw new ArgumentNullException(nameof(include))
            : source;

    /// <summary>
    /// Adds the fields the include lambda selects to the collection field this is called on.
    /// </summary>
    /// <typeparam name="T">The element type of the field the include is attached to.</typeparam>
    /// <param name="source">The collection field.</param>
    /// <param name="include">The fields to add, selected from an element.</param>
    /// <returns>The receiver, so the query continues from it.</returns>
    /// <remarks>
    /// The lambda is read from the expression tree by the translator; it is never compiled here, but a
    /// null one cannot be translated.
    /// </remarks>
    [GraphQLIncludes]
    public static T[] Includes<T>(this T[] source, Expression<Func<T, object>> include)
        where T : ICanHaveIncludes
        => include is null
            ? throw new ArgumentNullException(nameof(include))
            : source;
}
