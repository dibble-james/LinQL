namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis;

// The diagnostics a declared query produces when it cannot take part in entity caching.
//
// Entity caching is silent when it is absent: an operation that opts out is still sent, cached by document and
// variables, and simply never refreshed by a mutation. Nothing else would tell the author that the one thing they
// expected — a mutation updating the query they are watching — cannot happen, so the reason is reported on the
// declaration. A schema that declares no @key patterns reports nothing here: entity caching is then not
// configured, rather than declined for a reason a diagnostic could explain.
internal static class EntityCacheDiagnostics
{
    /// <summary>
    /// The selection covers a type the store cannot normalise.
    /// </summary>
    public static readonly DiagnosticDescriptor NotNormalisable = new(
        id: "LINQLGEN19",
        title: "Declared query cannot take part in entity caching",
        messageFormat: "The declared query '{0}' keeps the operation-level cache only: {1}. Nothing normalises this operation, so a mutation that changes what it reads does not refresh it.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// The selection covers an entity without the field that identifies it.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingEntityKey = new(
        id: "LINQLGEN20",
        title: "Declared query selects an entity without its key",
        messageFormat: "The declared query '{0}' reads '{1}' without selecting its key field '{2}', so the operation cannot be normalised and keeps the operation-level cache. Select '{2}' to have a mutation refresh it.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
