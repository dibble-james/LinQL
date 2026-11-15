namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using Microsoft.CodeAnalysis;

/// <summary>
/// The outcome of translating a declared query's lambda.
/// </summary>
/// <param name="Document">The document, or <see langword="null"/> when the declaration had a problem.</param>
/// <param name="Bindings">The variables each of the declaration's parameters fills.</param>
/// <param name="Problems">What stopped the translation, if anything.</param>
internal sealed record DeclaredQueryTranslation(
    DocumentNode? Document,
    IReadOnlyList<DeclaredQueryBinding> Bindings,
    IReadOnlyList<Diagnostic> Problems);

/// <summary>
/// One of a declared query's parameters and the variables it fills.
/// </summary>
/// <param name="Parameter">The lambda parameter's name.</param>
/// <param name="Variables">The variables the parameter fills, in the document's order.</param>
/// <remarks>
/// A parameter can fill more than one variable: attaching it to two arguments declares a variable per argument,
/// and the binder sends the one value under both names.
/// </remarks>
internal sealed record DeclaredQueryBinding(string Parameter, IReadOnlyList<string> Variables);
