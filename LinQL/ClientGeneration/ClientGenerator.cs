namespace LinQL.ClientGeneration;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Converts SDL into a set of types.
/// </summary>
[Generator]
public class ClientGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor StartingInfo = new(
        id: "LINQLGEN01",
        title: "Starting code generation",
        messageFormat: "Starting code generation for file {0}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingNamespace = new(
        id: "LINQLGEN02",
        title: "Missing namespace attribute",
        messageFormat: "GraphQL file {0} does not have a LinQLClientNamespace attribute. Please add it to the AdditionalFiles element in your CSProj file.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Takes a SDL input and generates a set of corresponding types.
    /// </summary>
    /// <param name="sdl">The SDL</param>
    /// <param name="graphNamespace">The namespace to put the types into.</param>
    /// <param name="extraUsings">Extra required namespaces required for the client to compile.</param>
    /// <returns>The generated types</returns>
    public static string Generate(string sdl, string graphNamespace, string[] extraUsings)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);

        var clientContext = new DocumentWalkerContext(graphNamespace, extraUsings);

        new DocumentWalker().Visit(document, clientContext);

        return clientContext.ToString();
    }

    /// <inheritdoc/>
    public void Execute(GeneratorExecutionContext context)
    {
        foreach (var schema in context.AdditionalFiles.Where(x => x.Path.EndsWith(".graphql")))
        {
            context.ReportDiagnostic(Diagnostic.Create(StartingInfo, Location.None, schema.Path));

            if (!context.AnalyzerConfigOptions.GetOptions(schema).TryGetValue("build_metadata.additionalfiles.LinQLClientNamespace", out var ns))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingNamespace, Location.None, schema.Path));
                continue;
            }

            context.AnalyzerConfigOptions.GetOptions(schema).TryGetValue("build_metadata.additionalfiles.LinQLExtraNamespaces", out var extraNamespaces);

            var extraUsings = extraNamespaces?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            var content = schema.GetText(context.CancellationToken)!.ToString();
            var schemaName = Path.GetFileName(schema.Path).Replace(".graphql", string.Empty);

            context.AddSource(
                $"{schemaName}.g.cs",
                SourceText.From(
                    Generate(content, ns, extraUsings),
                    Encoding.UTF8)
                );
        }
    }

    /// <inheritdoc/>
    public void Initialize(GeneratorInitializationContext context) { }
}
