namespace LinQL.ClientGeneration;

using System.Collections.Immutable;
using System.Globalization;
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

            // A declaration whose expression comes from a method has no source to read, so there is nothing to
            // translate. Reporting it is the only honest outcome: skipping it would leave the call site with no
            // binder and no reason why.
            if (query.Body is null)
            {
                output.ReportDiagnostic(Diagnostic.Create(DeclaredQueryDiagnostics.NotALambda, query.Location, query.FieldName));
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

            var translation = DeclaredQueryTranslator.Translate(query.Body, model, rootType, query.Values);

            foreach (var problem in translation.Problems)
            {
                output.ReportDiagnostic(problem);
            }

            if (translation.Document is not null)
            {
                EmitDeclaredQuery(output, query, translation, model);
            }
        }
    }

    /// <summary>
    /// Emits the document a declared query compiles to and the binder a call site uses to send it.
    /// </summary>
    /// <param name="output">The source output.</param>
    /// <param name="query">The declaration.</param>
    /// <param name="translation">Its document and the variables its parameters fill.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <remarks>
    /// The document does not depend on a call, so it is a constant; the binder takes the lambda's own parameters
    /// and binds them to the names the document gave the variables, returning the <c>LinQLQuery</c> an adapter
    /// sends. When the selection can be normalised, the operation's entity cache is emitted beside it and the
    /// binder attaches it, so the query takes part in entity caching without the call site changing.
    /// </remarks>
    private static void EmitDeclaredQuery(
        SourceProductionContext output,
        DeclaredQuery query,
        DeclaredQueryTranslation translation,
        SchemaModel model)
    {
        var name = query.ContainingType + query.FieldName;

        var cache = DeclaredQueryCache.Emit(
            translation.Document!,
            model,
            query.RootType!,
            query.ClientNamespace,
            name);

        if (cache is not null)
        {
            output.AddSource(
                query.Namespace is null ? $"{name}Cache.g.cs" : $"{query.Namespace}.{name}Cache.g.cs",
                SourceText.From(cache, Encoding.UTF8));
        }

        var document = FieldDeclaration(
                VariableDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)))
                    .AddVariables(
                        VariableDeclarator(Identifier("Document"))
                            .WithInitializer(EqualsValueClause(ParseExpression(SymbolDisplay.FormatLiteral(translation.Document!.ToString(), quote: true))))))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.ConstKeyword));

        var resultType = Qualify(query.ResultType!, model, query.ClientNamespace);
        var queryType = $"global::LinQL.LinQLQuery<global::{query.ClientNamespace}.{query.RootType}, {resultType}>";

        var binder = MethodDeclaration(ParseTypeName(queryType), SchemaNames.IdentifierFor(query.FieldName))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddParameterListParameters([.. query.Parameters.Select(x => Parameter(SchemaNames.IdentifierFor(x.Name)).WithType(ParseTypeName(Qualify(x.Type, model, query.ClientNamespace))))])
            .WithExpressionBody(ArrowExpressionClause(ParseExpression(BinderCall(query, queryType, translation, cache is not null))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

        var members = ClassDeclaration(Identifier(name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(document, binder);

        MemberDeclarationSyntax emitted = query.Namespace is null
            ? members
            : NamespaceDeclaration(ParseName(query.Namespace)).AddMembers(members);

        // A hint name is unique across the whole compilation, so it carries the namespace: the same containing
        // type and field name in two namespaces is a legal pair of declarations, and naming the file after the
        // type alone would fail the run instead of emitting both.
        output.AddSource(
            query.Namespace is null ? $"{name}.g.cs" : $"{query.Namespace}.{name}.g.cs",
            SourceText.From(emitted.NormalizeWhitespace().ToFullString(), Encoding.UTF8));
    }

    /// <summary>
    /// Writes the call that joins a document to its operation, its projection and its values.
    /// </summary>
    /// <param name="query">The declaration.</param>
    /// <param name="queryType">The type the binder returns.</param>
    /// <param name="translation">Its operation and the variables its parameters fill.</param>
    /// <param name="cached">Whether the operation has an entity cache to attach.</param>
    /// <returns>The call, with one tuple per variable.</returns>
    private static string BinderCall(
        DeclaredQuery query,
        string queryType,
        DeclaredQueryTranslation translation,
        bool cached)
    {
        List<string> arguments =
        [
            "Document",
            $"global::LinQL.Description.RootOperationType.{translation.Operation}",
            Projection(query),
        ];

        foreach (var binding in translation.Bindings)
        {
            arguments.Add($"({SymbolDisplay.FormatLiteral(binding.Variable, quote: true)}, {BoundValue(binding.Value)})");
        }

        var call = $"{queryType}.Bind({string.Join(", ", arguments)})";

        return cached
            ? $"{call}.WithCache({query.ContainingType}{query.FieldName}Cache.Instance)"
            : call;
    }

    /// <summary>
    /// Writes the expression that produces a variable's value at call time.
    /// </summary>
    /// <param name="value">The value the translation recorded.</param>
    /// <returns>The expression, fully qualified so the generated file needs no usings.</returns>
    /// <remarks>
    /// A filter is one variable holding the whole filter, so its value is built here rather than read from a
    /// parameter. Emitting that construction is what makes a filter's values follow the declaration's parameters
    /// without the document depending on them.
    /// </remarks>
    private static string BoundValue(DeclaredQueryBoundValue value) => value switch
    {
        DeclaredQueryParameterValue parameter => $"(object?){SchemaNames.IdentifierFor(parameter.Parameter).Text}",
        DeclaredQueryConstantValue constant => $"(object?){ConstantLiteral(constant.Constant)}",
        DeclaredQueryListValue list => $"(object?)new object?[] {{ {string.Join(", ", list.Items.Select(BoundValue))} }}",
        DeclaredQueryFilterValue filter => FilterValue(filter),
        _ => "(object?)null",
    };

    /// <summary>
    /// Writes the calls that rebuild a filter through <c>FilterValue</c>.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The expression.</returns>
    /// <remarks>
    /// The calls mirror the runtime translator: a combinator folds what was built so far into its first branch,
    /// and every other field is set under the name the schema gives it.
    /// </remarks>
    private static string FilterValue(DeclaredQueryFilterValue filter)
    {
        var code = "global::LinQL.Filters.FilterValue.Create()";

        foreach (var field in filter.Fields)
        {
            var name = SymbolDisplay.FormatLiteral(field.Name, quote: true);

            code += field.IsList && field.Value is DeclaredQueryFilterValue nested
                ? $".Combine({name}, {FilterValue(nested)})"
                : $".Set({name}, {BoundValue(field.Value)})";
        }

        return code;
    }

    /// <summary>
    /// Writes a constant as a C# literal of the same type.
    /// </summary>
    /// <param name="constant">The constant.</param>
    /// <returns>The literal, in the invariant culture.</returns>
    private static string ConstantLiteral(object? constant) => constant switch
    {
        null => "null",
        string text => SymbolDisplay.FormatLiteral(text, quote: true),
        char character => SymbolDisplay.FormatLiteral(character, quote: true),
        bool flag => flag ? "true" : "false",
        decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
        uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null",
        _ => "null",
    };

    /// <summary>
    /// Qualifies a type a declaration names against the schema's client namespace.
    /// </summary>
    /// <param name="type">The type as the compiler displayed it.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <param name="clientNamespace">The client namespace the schema declares.</param>
    /// <returns>The type, qualified when the schema declares it.</returns>
    /// <remarks>
    /// A declaration's projection and parameters may name the types the generator emits, which the semantic
    /// model the generator reads a declaration from cannot resolve: they are that run's output. Such a name
    /// comes through unqualified, so a name the schema declares is completed with the namespace its client is
    /// emitted into. A name the caller already qualified, or one the schema does not declare, is left alone.
    /// </remarks>
    private static string Qualify(string type, SchemaModel model, string clientNamespace)
    {
        var nullable = type.EndsWith("?", StringComparison.Ordinal) ? "?" : string.Empty;
        var name = nullable.Length == 0 ? type : type.Substring(0, type.Length - 1);
        var dimensions = 0;

        while (name.EndsWith("[]", StringComparison.Ordinal))
        {
            name = name.Substring(0, name.Length - 2);
            dimensions++;
        }

        if (name.Length == 0 || name.Contains('.') || name.Contains("::") || !model.DeclaredTypesByName.ContainsKey(name))
        {
            return type;
        }

        var array = string.Concat(Enumerable.Repeat("[]", dimensions));

        return $"global::{clientNamespace}.{name}{array}{nullable}";
    }

    /// <summary>
    /// Names the declared field a call's projection comes from.
    /// </summary>
    /// <param name="query">The declaration.</param>
    /// <returns>The field, fully qualified so the generated file needs no usings.</returns>
    private static string Projection(DeclaredQuery query)
        => query.Namespace is null
            ? $"global::{query.ContainingType}.{query.FieldName}"
            : $"global::{query.Namespace}.{query.ContainingType}.{query.FieldName}";

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
