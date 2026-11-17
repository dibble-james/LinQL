namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis;

/// <summary>
/// The outcome of translating a declared query's lambda.
/// </summary>
internal sealed record DeclaredQueryTranslation(
    DocumentNode? Document,
    RootOperationType Operation,
    IReadOnlyList<DeclaredQueryBinding> Bindings,
    IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// One variable a declared query declares and the value the binder computes for it.
/// </summary>
internal sealed record DeclaredQueryBinding(string Variable, DeclaredQueryBoundValue Value);

/// <summary>
/// A value the generated binder produces when a declared query is sent.
/// </summary>
/// <remarks>
/// A document's values are not all one of its parameters: a filter is a single variable holding a whole
/// dictionary, and its entries may read several parameters, so the binder needs the shape of the value rather
/// than a parameter-to-variable-name table.
/// </remarks>
internal abstract record DeclaredQueryBoundValue;

/// <summary>
/// One of the declaration's parameters, passed through as the variable's value.
/// </summary>
internal sealed record DeclaredQueryParameterValue(string Parameter) : DeclaredQueryBoundValue;

/// <summary>
/// A constant the compiler folded, written into the binder.
/// </summary>
internal sealed record DeclaredQueryConstantValue(object? Constant) : DeclaredQueryBoundValue;

/// <summary>
/// A list of values the binder builds item by item.
/// </summary>
internal sealed record DeclaredQueryListValue(IReadOnlyList<DeclaredQueryBoundValue> Items) : DeclaredQueryBoundValue;

/// <summary>
/// A filter the binder rebuilds through <c>FilterValue</c> on every call.
/// </summary>
/// <remarks>
/// Hot Chocolate takes a filter as one <c>where</c> variable holding nested dictionaries, so the document cannot
/// carry a variable per value the way an ordinary argument can. The entries are therefore emitted as calls on
/// <c>FilterValue</c>, which produces the same shape the runtime translator builds from an expression.
/// </remarks>
internal sealed record DeclaredQueryFilterValue(IReadOnlyList<DeclaredQueryFilterField> Fields) : DeclaredQueryBoundValue;

/// <summary>
/// One field of a filter, and the value it is set to.
/// </summary>
internal sealed record DeclaredQueryFilterField(string Name, bool IsList, DeclaredQueryBoundValue Value);
