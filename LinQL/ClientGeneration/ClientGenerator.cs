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

    private static readonly DiagnosticDescriptor UnknownTypesAs = new(
        id: "LINQLGEN07",
        title: "Unknown LinQLTypesAs value",
        messageFormat: "LinQLTypesAs value '{0}' is not recognised; generating classes. Use 'Class' or 'Record'.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string TypesAsProperty = "build_property.LinQLTypesAs";

    /// <summary>
    /// Takes a SDL input and generates a set of corresponding types.
    /// </summary>
    /// <param name="output">The analyser output.</param>
    /// <param name="path">The file being parsed.</param>
    /// <param name="sdl">The SDL</param>
    /// <param name="graphNamespace">The namespace to put the types into.</param>
    /// <param name="extraUsings">Extra required namespaces required for the client to compile.</param>
    /// <param name="typesAs">Whether the generated types are classes or records.</param>
    /// <returns>The generated types</returns>
    public static string Generate(SourceProductionContext output, string path, string sdl, string graphNamespace, string[] extraUsings, LinQLTypesAs typesAs = LinQLTypesAs.Class)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);

        var clientContext = new DocumentWalkerContext(path, output, graphNamespace, extraUsings, typesAs);

        new DocumentWalker().Visit(document, clientContext);

        return clientContext.ToString();
    }

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider.Where(static x => x.Path.EndsWith(".graphql") && !x.Path.EndsWith(".extensions.graphql"));
        var extensions = context.AdditionalTextsProvider.Where(static x => x.Path.EndsWith(".extensions.graphql")).Collect();

        var typesAsSetting = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
        {
            provider.GlobalOptions.TryGetValue(TypesAsProperty, out var value);
            return value;
        });

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
        )).Combine(typesAsSetting).Select((schema, _) =>
        (
            schema.Left.Path,
            schema.Left.Contents,
            schema.Left.Options,
            schema.Left.Extensions,
            TypesAs: schema.Right
        ));

        context.RegisterSourceOutput(schemas, (output, schema) =>
        {
            output.ReportDiagnostic(Diagnostic.Create(StartingInfo, Location.None, schema.Path));

            if (!schema.Options.TryGetValue("build_metadata.additionalfiles.LinQLClientNamespace", out var ns))
            {
                output.ReportDiagnostic(Diagnostic.Create(MissingNamespace, Location.None, schema.Path));
                return;
            }

            if (!TryParseTypesAs(schema.TypesAs, out var typesAs))
            {
                output.ReportDiagnostic(Diagnostic.Create(UnknownTypesAs, Location.None, schema.TypesAs));
            }

            schema.Options.TryGetValue("build_metadata.additionalfiles.LinQLExtraNamespaces", out var extraNamespaces);

            var extraUsings = extraNamespaces?.Split([';'], StringSplitOptions.RemoveEmptyEntries) ?? [];

            var schemaName = Path.GetFileNameWithoutExtension(schema.Path);

            var sdl = string.Join("\n", schema.Contents!.ToString(), schema.Extensions?.ToString() ?? string.Empty);

            output.AddSource(
                $"{schemaName}.g.cs",
                SourceText.From(
                    Generate(output, schema.Path, sdl, ns, extraUsings, typesAs),
                    Encoding.UTF8)
                );
        });
    }

    /// <summary>
    /// Reads the <c>LinQLTypesAs</c> property.
    /// </summary>
    /// <param name="value">The raw property value, or <see langword="null"/> when it was not set.</param>
    /// <param name="typesAs">The kind of declaration to generate.</param>
    /// <returns><see langword="true"/> when the value was understood, so an unrecognised one can be reported.</returns>
    private static bool TryParseTypesAs(string? value, out LinQLTypesAs typesAs)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, nameof(LinQLTypesAs.Class), StringComparison.OrdinalIgnoreCase))
        {
            typesAs = LinQLTypesAs.Class;
            return true;
        }

        if (string.Equals(trimmed, nameof(LinQLTypesAs.Record), StringComparison.OrdinalIgnoreCase))
        {
            typesAs = LinQLTypesAs.Record;
            return true;
        }

        typesAs = LinQLTypesAs.Class;
        return false;
    }
}
