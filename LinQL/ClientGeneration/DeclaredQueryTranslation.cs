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

// A value the generated binder produces when a declared query is sent.
//
// A document's values are not all one of its parameters: a filter is a single variable holding a whole dictionary,
// and its entries may read several parameters, so the binder needs the shape of the value rather than a parameter-
// to-variable-name table.
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

// A filter the binder rebuilds through FilterValue on every call.
//
// Hot Chocolate takes a filter as one where variable holding nested dictionaries, so the document cannot carry a
// variable per value the way an ordinary argument can. The entries are therefore emitted as calls on FilterValue,
// which produces the same shape the runtime translator builds from an expression.
internal sealed record DeclaredQueryFilterValue(IReadOnlyList<DeclaredQueryFilterField> Fields) : DeclaredQueryBoundValue;

/// <summary>
/// One field of a filter, and the value it is set to.
/// </summary>
internal sealed record DeclaredQueryFilterField(string Name, bool IsList, DeclaredQueryBoundValue Value);
