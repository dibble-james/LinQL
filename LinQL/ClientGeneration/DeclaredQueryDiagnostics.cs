namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis;

/// <summary>
/// The diagnostics a declared query can produce.
/// </summary>
/// <remarks>
/// Every one of them is reported on the declaration, so a query that the generator cannot compile is not
/// silently left out of the result. The translator's own rules take LINQLGEN08 to 11; binding a declaration to
/// a schema takes 12 to 14.
/// </remarks>
internal static class DeclaredQueryDiagnostics
{
    /// <summary>
    /// The field returns a type whose selection has to be written out.
    /// </summary>
    public static readonly DiagnosticDescriptor ImplicitSelection = new(
        id: "LINQLGEN08",
        title: "Declared query selects a field implicitly",
        messageFormat: "'{0}' returns '{1}', so it needs an explicit selection. Call .Select(...) or .SelectAll() on it: a declared query cannot expand a selection from the result type at build time.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The translator does not understand a construct the declaration uses.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedConstruct = new(
        id: "LINQLGEN09",
        title: "Declared query uses a construct the translator does not support",
        messageFormat: "'{0}' cannot be translated: {1}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The projection reads one of the declaration's values.
    /// </summary>
    public static readonly DiagnosticDescriptor ProjectionReadsValue = new(
        id: "LINQLGEN10",
        title: "Declared query's projection reads one of its values",
        messageFormat: "'{0}' is one of the declaration's values, so it can only be written into the document as an argument. A projection has to be a function of the response alone, because the document does not depend on the values.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// An argument's value is one the declaration cannot write into its document.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedValue = new(
        id: "LINQLGEN11",
        title: "Declared query uses a value that cannot be written into its document",
        messageFormat: "'{0}' cannot become a value: {1}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The declaration names a schema the project does not generate.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownSchema = new(
        id: "LINQLGEN12",
        title: "Declared query names an unknown schema",
        messageFormat: "The declared query '{0}' names schema '{1}', but no SDL in this project declares that client namespace. Use the LinQLClientNamespace of the schema the query is written against.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The named schema does not generate the root type the declaration uses.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownRootType = new(
        id: "LINQLGEN13",
        title: "Declared query uses a root type the schema does not generate",
        messageFormat: "Schema '{0}' does not generate a root type named '{1}', which the declared query '{2}' uses as its root. Use the root type of the schema the query is written against.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The declaration is not a static expression field.
    /// </summary>
    public static readonly DiagnosticDescriptor NotAnExpression = new(
        id: "LINQLGEN14",
        title: "Declared query is not a parameterised expression",
        messageFormat: "'{0}' is marked with LinQLQuery but is not a static field of type Expression<Func<TRoot, TData>>. A declared query is a static field holding a parameterised expression.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
