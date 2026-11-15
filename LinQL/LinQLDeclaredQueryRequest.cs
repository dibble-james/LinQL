namespace LinQL;

using System;
using System.Collections.Generic;
using System.Linq;
using LinQL.Filters;

/// <summary>
/// A declared query's document together with the values a call bound to its variables.
/// </summary>
/// <param name="Document">The document generated from the declaration, which does not depend on the call.</param>
/// <param name="Variables">The values the document's variables are bound to, keyed by the names it uses.</param>
/// <remarks>
/// The document is precomputed, so two calls that bind different values send the same document and differ only
/// in <paramref name="Variables"/>. The projection and the adapter overloads that carry it are the next step; a
/// declared query's generated binder returns this in the meantime.
/// </remarks>
public record LinQLDeclaredQueryRequest(string Document, IReadOnlyDictionary<string, object?> Variables)
{
    /// <summary>
    /// Binds values to the variables a generated document declares.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="values">The value of each variable, by the name the document uses.</param>
    /// <returns>The request.</returns>
    /// <remarks>
    /// Values are normalised the way the runtime translator normalises an attached argument's value, so an input
    /// object reaches the wire in camelCase with enums by name and a filter as the dictionaries Hot Chocolate
    /// expects, rather than in CLR shape.
    /// </remarks>
    public static LinQLDeclaredQueryRequest Bind(string document, params (string Variable, object? Value)[] values)
        => new(document, values.ToDictionary(x => x.Variable, x => FilterValue.Normalize(x.Value), StringComparer.Ordinal));
}
