namespace LinQL.ClientGeneration;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

/// <summary>
/// Converts SDL into a <see cref="Graph"/>.
/// </summary>
[Generator]
public class ClientGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor InvalidXmlWarning = new(
        id: "LINQLGEN01",
        title: "Starting code generation",
        messageFormat: "Starting code generation for file {0}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Takes a SDL input and generates a <see cref="Graph"/>.
    /// </summary>
    /// <param name="sdl">The SDL</param>
    /// <param name="graphName">The name of the <see cref="Graph"/>.</param>
    /// <param name="graphNamespace">The namespace to put the <see cref="Graph"/>.</param>
    /// <returns>The generated <see cref="Graph"/></returns>
    public static string Generate(string sdl, string graphName, string graphNamespace)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);

        var clientContext = new DocumentWalkerContext(graphName, graphNamespace);

        new DocumentWalker().Visit(document, clientContext);

        return clientContext.ToString();
    }

    /// <inheritdoc/>
    public void Execute(GeneratorExecutionContext context)
    {

        foreach (var schema in context.AdditionalFiles.Where(x => x.Path.EndsWith(".graphql")))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidXmlWarning, Location.None, schema.Path));
            var content = schema.GetText(context.CancellationToken)!.ToString();
            var schemaName = Path.GetFileName(schema.Path).Replace(".graphql", string.Empty);

            context.AddSource(
                $"{schemaName}.g.cs",
                SourceText.From(
                    Generate(content, schemaName, schemaName + ".Client"),
                    Encoding.UTF8)
                );
        }
    }

    /// <inheritdoc/>
    public void Initialize(GeneratorInitializationContext context) { }
}
