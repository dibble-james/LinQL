namespace LinQL.ClientGeneration;

using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Converts SDL into a set of types.
/// </summary>
[Generator]
public class ClientGenerator : IIncrementalGenerator
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
        messageFormat: "GraphQL file {0} does not have a LinQLClientNamespace attribute. Please add it to the AdditionalFiles element in your csproj file.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Takes a SDL input and generates a set of corresponding types.
    /// </summary>
    /// <param name="output">The analyser output.</param>
    /// <param name="path">The file being parsed.</param>
    /// <param name="sdl">The SDL</param>
    /// <param name="graphNamespace">The namespace to put the types into.</param>
    /// <param name="extraUsings">Extra required namespaces required for the client to compile.</param>
    /// <returns>The generated types</returns>
    public static string Generate(SourceProductionContext output, string path, string sdl, string graphNamespace, string[] extraUsings)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);

        var clientContext = new DocumentWalkerContext(path, output, graphNamespace, extraUsings);

        new DocumentWalker().Visit(document, clientContext);

        return clientContext.ToString();
    }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider.Where(static x => x.Path.EndsWith(".graphql") && !x.Path.EndsWith(".extensions.graphql"));
        var extensions = context.AdditionalTextsProvider.Where(static x => x.Path.EndsWith(".extensions.graphql")).Collect();

        var schemas = files.Combine(context.AnalyzerConfigOptionsProvider).Select((file, ct) =>
        (
            File: file.Left,
            Options: file.Right.GetOptions(file.Left)
        )).Combine(extensions).Select((file, ct) =>
        (
            file.Left.File.Path,
            Contents: file.Left.File.GetText(ct),
            file.Left.Options,
            Extensions: file.Right.FirstOrDefault(f => f.Path.Replace(".extensions.graphql", ".graphql") == file.Left.File.Path)?.GetText(ct)
        ));

        context.RegisterSourceOutput(schemas, (output, schema) =>
        {
            output.ReportDiagnostic(Diagnostic.Create(StartingInfo, Location.None, schema.Path));

            if (!schema.Options.TryGetValue("build_metadata.additionalfiles.LinQLClientNamespace", out var ns))
            {
                output.ReportDiagnostic(Diagnostic.Create(MissingNamespace, Location.None, schema.Path));
                return;
            }

            schema.Options.TryGetValue("build_metadata.additionalfiles.LinQLExtraNamespaces", out var extraNamespaces);

            var extraUsings = extraNamespaces?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

            var schemaName = Path.GetFileNameWithoutExtension(schema.Path);

            var sdl = string.Join("\n", schema.Contents!.ToString(), schema.Extensions?.ToString() ?? string.Empty);

            output.AddSource(
                $"{schemaName}.g.cs",
                SourceText.From(
                    Generate(output, schema.Path, sdl, ns, extraUsings),
                    Encoding.UTF8)
                );
        });
    }
}
