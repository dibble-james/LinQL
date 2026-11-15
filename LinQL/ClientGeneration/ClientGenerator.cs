namespace LinQL.ClientGeneration;

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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

    private const string DeclaredQueryAttributeName = "LinQL.Description.LinQLQueryAttribute";

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

        // A declared query is bound by the client namespace of the schema it is written against, so every
        // schema's namespace and root types are collected and compared with what each declaration uses.
        var schemaBindings = schemas
            .Select(static (schema, _) =>
            {
                var sdl = string.Join("\n", schema.Contents?.ToString(), schema.Extensions?.ToString() ?? string.Empty);

                return (
                    Namespace: schema.Options.TryGetValue("build_metadata.additionalfiles.LinQLClientNamespace", out var clientNamespace) ? clientNamespace : null,
                    Roots: ReadRootTypes(sdl),
                    sdl,
                    schema.TypesAs);
            })
            .Collect();

        var declaredQueries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclaredQueryAttributeName,
                static (node, _) => node is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: FieldDeclarationSyntax } },
                static (attribute, _) => DeclaredQuery.Find(attribute))
            .Where(static query => query is not null)
            .Select(static (query, _) => query!)
            .Collect();

        context.RegisterSourceOutput(
            schemaBindings.Combine(declaredQueries),
            static (output, bound) => ReportDeclaredQueries(output, bound.Left, bound.Right));

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
    /// Reads the root type names an SDL declares.
    /// </summary>
    /// <param name="sdl">The SDL.</param>
    /// <returns>The names, joined with <c>;</c> so that the same schema compares equal between runs.</returns>
    private static string ReadRootTypes(string sdl)
    {
        var schema = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl).Definitions
            .OfType<HotChocolate.Language.SchemaDefinitionNode>()
            .FirstOrDefault();

        return schema is null
            ? string.Empty
            : string.Join(";", schema.OperationTypes.Select(x => x.Type.Name.Value));
    }

    /// <summary>
    /// Reports the declared queries that this project's schemas cannot satisfy.
    /// </summary>
    /// <param name="output">The source output.</param>
    /// <param name="schemas">Every schema's client namespace and root type names.</param>
    /// <param name="queries">The declarations found.</param>
    private static void ReportDeclaredQueries(
        SourceProductionContext output,
        ImmutableArray<(string? Namespace, string Roots, string Sdl, string? TypesAs)> schemas,
        ImmutableArray<DeclaredQuery> queries)
    {
        if (queries.IsDefaultOrEmpty)
        {
            return;
        }

        var roots = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var declarations = new Dictionary<string, (string Sdl, string? TypesAs)>(StringComparer.Ordinal);
        var models = new Dictionary<string, SchemaModel>(StringComparer.Ordinal);

        foreach (var (clientNamespace, declaredRoots, sdl, typesAs) in schemas)
        {
            if (clientNamespace is null)
            {
                // An SDL without LinQLClientNamespace already reports LINQLGEN02, so it binds nothing here.
                continue;
            }

            roots[clientNamespace] = [.. declaredRoots.Split([';'], StringSplitOptions.RemoveEmptyEntries)];
            declarations[clientNamespace] = (sdl, typesAs);
        }

        foreach (var query in queries)
        {
            if (!query.IsStatic || query.RootType is not { } rootType)
            {
                output.ReportDiagnostic(Diagnostic.Create(DeclaredQueryDiagnostics.NotAnExpression, query.Location, query.FieldName));
                continue;
            }

            if (!roots.TryGetValue(query.ClientNamespace, out var rootTypes))
            {
                output.ReportDiagnostic(Diagnostic.Create(DeclaredQueryDiagnostics.UnknownSchema, query.Location, query.FieldName, query.ClientNamespace));
                continue;
            }

            if (!rootTypes.Contains(rootType))
            {
                output.ReportDiagnostic(Diagnostic.Create(DeclaredQueryDiagnostics.UnknownRootType, query.Location, query.ClientNamespace, rootType, query.FieldName));
                continue;
            }

            // Only a declaration bound to a schema has a document to translate, and the schema is built once
            // per namespace however many declarations name it.
            if (!models.TryGetValue(query.ClientNamespace, out var model))
            {
                var (schemaSdl, schemaTypesAs) = declarations[query.ClientNamespace];

                _ = TryParseTypesAs(schemaTypesAs, out var typesAs);

                model = CreateModel(schemaSdl, query.ClientNamespace, typesAs);
                models[query.ClientNamespace] = model;
            }

            var (document, problems) = DeclaredQueryTranslator.Translate(query.Body, model, rootType);

            foreach (var problem in problems)
            {
                output.ReportDiagnostic(problem);
            }

            if (document is not null)
            {
                EmitDocument(output, query, document);
            }
        }
    }

    /// <summary>
    /// Emits the document a declared query compiles to, so a caller can read it.
    /// </summary>
    /// <param name="output">The source output.</param>
    /// <param name="query">The declaration.</param>
    /// <param name="document">Its document.</param>
    /// <remarks>
    /// The document is a constant for now. Once the binder lands it is joined by the variable mapping and the
    /// projection, which is why it is emitted beside the declaration rather than as a request object here.
    /// </remarks>
    private static void EmitDocument(SourceProductionContext output, DeclaredQuery query, HotChocolate.Language.DocumentNode document)
    {
        var name = query.ContainingType + query.FieldName;

        var members = ClassDeclaration(Identifier(name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(
                FieldDeclaration(
                    VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
                        .AddVariables(
                            VariableDeclarator(Identifier("Document"))
                                .WithInitializer(EqualsValueClause(ParseExpression(SymbolDisplay.FormatLiteral(document.ToString(), quote: true))))))
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.ConstKeyword)));

        MemberDeclarationSyntax emitted = query.Namespace is null
            ? members
            : NamespaceDeclaration(ParseName(query.Namespace)).AddMembers(members);

        output.AddSource(
            $"{name}.Document.g.cs",
            SourceText.From(emitted.NormalizeWhitespace().ToFullString(), Encoding.UTF8));
    }

    /// <summary>
    /// Builds the schema model an SDL describes, without emitting a client from it.
    /// </summary>
    /// <param name="sdl">The SDL.</param>
    /// <param name="graphNamespace">The client namespace the schema declares.</param>
    /// <param name="typesAs">The kind of declaration the schema generates.</param>
    /// <returns>The model.</returns>
    /// <remarks>
    /// The walk is turned away from reporting: the client's own walk already reports the filter diagnostics,
    /// and repeating them once per declared query would multiply them.
    /// </remarks>
    internal static SchemaModel CreateModel(string sdl, string graphNamespace, LinQLTypesAs typesAs)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);
        var context = new DocumentWalkerContext("declared-queries", default, graphNamespace, [], typesAs)
        {
            ReportsDiagnostics = false,
        };

        new DocumentWalker().Visit(document, context);

        return context.Model;
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
