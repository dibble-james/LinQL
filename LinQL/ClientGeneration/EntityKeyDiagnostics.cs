namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis;

// The diagnostics an entity key pattern can produce.
//
// A pattern that cannot identify an entity is reported rather than ignored, because the feature it opts into is
// silent when it is absent: a type that is not an entity is never normalised, and nothing else would tell the
// author why.
internal static class EntityKeyDiagnostics
{
    /// <summary>
    /// The pattern names more than one field, or a nested selection.
    /// </summary>
    public static readonly DiagnosticDescriptor MultipleFields = new(
        id: "LINQLGEN16",
        title: "Entity key names more than one field",
        messageFormat: "The @key on {0} names '{1}'. Only a single leaf field is supported, so the pattern is ignored and its types are not cached as entities.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The pattern names a field the type does not declare.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownField = new(
        id: "LINQLGEN17",
        title: "Entity key names a field the type does not declare",
        messageFormat: "The @key on '{0}' names '{1}', which the type does not declare. The pattern is ignored, so the type is not cached as an entity.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The pattern names a field that is not a leaf.
    /// </summary>
    public static readonly DiagnosticDescriptor NotALeafField = new(
        id: "LINQLGEN18",
        title: "Entity key names a field that is not a leaf",
        messageFormat: "The @key on '{0}' names '{1}', which is not a leaf field. Only shallow leaf fields identify an entity, so the pattern is ignored and the type is not cached as an entity.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
