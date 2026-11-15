namespace LinQL.Tests.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using LinQL.ClientGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

public class ClientGeneratorDiagnosticTests
{
    private const string MissingFilterInputSdl = """
        schema {
          query: Query
        }

        type Query {
          products(where: ProductFilterInput): [Product!]!
        }

        type Product {
          name: String!
        }
        """;

    private const string DeclaredFilterInputSdl = """
        schema {
          query: Query
        }

        type Query {
          products(where: ProductFilterInput): [Product!]!
        }

        type Product {
          name: String!
        }

        input ProductFilterInput {
          name: StringOperationFilterInput
        }

        input StringOperationFilterInput {
          eq: String
        }
        """;

    private const string ConflictingFilterArgumentSdl = """
        schema {
          query: Query
        }

        type Query {
          products(where: ProductFilterInput): [Product!]!
          recommended(filter: ProductFilterInput): [Product!]!
        }

        type Product {
          name: String!
        }

        input ProductFilterInput {
          name: StringOperationFilterInput
        }

        input StringOperationFilterInput {
          eq: String
        }
        """;

    private const string AgreeingFilterArgumentSdl = """
        schema {
          query: Query
        }

        type Query {
          products(where: ProductFilterInput): [Product!]!
          recommended(where: ProductFilterInput): [Product!]!
        }

        type Product {
          name: String!
        }

        input ProductFilterInput {
          name: StringOperationFilterInput
        }

        input StringOperationFilterInput {
          eq: String
        }
        """;

    [Fact]
    public void MissingFilterInputTypeIsReported()
    {
        var diagnostics = Generate(MissingFilterInputSdl);

        var diagnostic = Assert.Single(diagnostics, x => x.Id == "LINQLGEN05");

        Assert.Contains("'where'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'products'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'ProductFilterInput'", diagnostic.GetMessage(), StringComparison.Ordinal);

        // The diagnostic points at the argument itself, not at the file.
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;

        Assert.Equal(5, position.Line);
        Assert.Equal(11, position.Character);
    }

    [Fact]
    public void DeclaredFilterInputTypeIsNotReported()
        => Assert.DoesNotContain(Generate(DeclaredFilterInputSdl), x => x.Id == "LINQLGEN05");

    [Fact]
    public void ConflictingFilterArgumentNameIsReported()
    {
        // Both fields return Product[], so one generated Filter method serves both: the second field's
        // argument name cannot be expressed and would be silently ignored.
        var diagnostics = Generate(ConflictingFilterArgumentSdl);

        var diagnostic = Assert.Single(diagnostics, x => x.Id == "LINQLGEN06");

        Assert.Contains("'filter'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'recommended'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'where'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'products'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Product[]'", diagnostic.GetMessage(), StringComparison.Ordinal);

        // The diagnostic points at the argument that is ignored, not at the file.
        var position = diagnostic.Location.GetLineSpan().StartLinePosition;

        Assert.Equal(6, position.Line);
        Assert.Equal(14, position.Character);
    }

    [Fact]
    public void AgreeingFilterArgumentNamesAreNotReported()
        => Assert.DoesNotContain(Generate(AgreeingFilterArgumentSdl), x => x.Id == "LINQLGEN06");

    [Fact]
    public void TypesAsBuildPropertySelectsRecordGeneration()
    {
        // Asserts the property reaches the generator through the same build_property channel
        // LinQL.props declares, rather than only being reachable from Generate. The schema has no
        // filter inputs, so every generated type declaration is a schema type.
        var source = string.Join("\n", Run(PlainSdl, "Record").GeneratedTrees.Select(x => x.ToString()));

        Assert.Contains("public partial record Character", source, StringComparison.Ordinal);
        Assert.Contains("public partial record Query : RootType<Query>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public partial class", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownTypesAsValueIsReported()
    {
        var diagnostic = Assert.Single(Generate(DeclaredFilterInputSdl, "Structs"), x => x.Id == "LINQLGEN07");

        Assert.Contains("'Structs'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownTypesAsValuesAreNotReported()
    {
        Assert.DoesNotContain(Generate(DeclaredFilterInputSdl, "Class"), x => x.Id == "LINQLGEN07");
        Assert.DoesNotContain(Generate(DeclaredFilterInputSdl, "record"), x => x.Id == "LINQLGEN07");
    }

    [Fact]
    public void DeclaredQueryBoundToItsSchemaIsNotReported()
        => Assert.DoesNotContain(
            Generate(PlainSdl, source: DeclaredQuerySource("Filtering")),
            x => x.Id is "LINQLGEN12" or "LINQLGEN13" or "LINQLGEN14");

    [Fact]
    public void DeclaredQueryNamingAnUnknownSchemaIsReported()
    {
        var diagnostic = Assert.Single(Generate(PlainSdl, source: DeclaredQuerySource("Nope")), x => x.Id == "LINQLGEN12");

        Assert.Contains("'Value'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Nope'", diagnostic.GetMessage(), StringComparison.Ordinal);

        // The diagnostic points at the declared field, not at the file.
        Assert.Equal(21, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void DeclaredQueryUsingARootTypeTheSchemaDoesNotGenerateIsReported()
    {
        var diagnostic = Assert.Single(Generate(PlainSdl, source: DeclaredQuerySource("Filtering", "Product")), x => x.Id == "LINQLGEN13");

        Assert.Contains("'Filtering'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Product'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Value'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredQuerySelectingATypeFieldImplicitlyIsReported()
    {
        var diagnostic = Assert.Single(Generate(PlainSdl, source: DeclaredQuerySource("Filtering", body: "x.Hero")), x => x.Id == "LINQLGEN08");

        Assert.Contains("'hero'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'Character'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredQueryDocumentIsEmitted()
        => Snapshot.Match(Assert.Single(
            Run(PlainSdl, source: DeclaredQuerySource("Filtering", body: "x.Hero.Select(h => new { h.Name })"))
                .GeneratedTrees,
            x => x.FilePath.Contains("DeclarationsValue.Document", StringComparison.Ordinal)).ToString());

    [Fact]
    public void DeclaredQueryArgumentsBecomeVariables()
        => Snapshot.Match(Assert.Single(
            Run(PlainSdl, source: DeclaredQuerySource("Filtering", body: "x.ExecuteCharacter(id).Select(c => new { c.Name })", values: "id"))
                .GeneratedTrees,
            x => x.FilePath.Contains("DeclarationsValue.Document", StringComparison.Ordinal)).ToString());

    [Fact]
    public void DeclarationWithAProblemEmitsNoDocument()
        => Assert.DoesNotContain(
            Run(PlainSdl, source: DeclaredQuerySource("Filtering", body: "x.Hero")).GeneratedTrees,
            x => x.FilePath.Contains("Document", StringComparison.Ordinal));

    [Fact]
    public void DeclarationThatIsNotAParameterisedExpressionIsReported()
    {
        var diagnostic = Assert.Single(Generate(PlainSdl, source: NotAQuerySource), x => x.Id == "LINQLGEN14");

        Assert.Contains("'Value'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private const string NotAQuerySource = """
        using LinQL.Description;

        namespace Queries
        {
            public static class Declarations
            {
                [LinQLQuery("Filtering")]
                public const string Value = "not a query";
            }
        }
        """;

    private const string PlainSdl = """
        schema {
          query: Query
        }

        type Query {
          hero: Character
          character(id: ID!): Character
        }

        type Character {
          name: String!
        }
        """;

    private static ImmutableArray<Diagnostic> Generate(string sdl, string? typesAs = null, string? source = null)
        => [.. Run(sdl, typesAs, source).Diagnostics];

    private static GeneratorDriverRunResult Run(string sdl, string? typesAs = null, string? source = null)
    {
        SyntaxTree[] trees = source is null ? [] : [CSharpSyntaxTree.ParseText(source)];

        return CSharpGeneratorDriver.Create(
            generators: [new ClientGenerator().AsSourceGenerator()],
            additionalTexts: [new SdlFile("SDLs/Filtering.graphql", sdl)],
            optionsProvider: new NamespaceOptions(typesAs))
        .RunGenerators(CSharpCompilation.Create(
            "Filtering",
            trees,
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)))
        .GetRunResult();
    }

    /// <summary>
    /// The references a declared query needs to be understood: the framework, and LinQL itself for the
    /// attribute and the root type it names.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => MetadataReference.CreateFromFile(x)),
        MetadataReference.CreateFromFile(typeof(LinQLOptions).Assembly.Location),
    ];

    /// <summary>
    /// Builds a source file declaring one query.
    /// </summary>
    /// <param name="clientNamespace">The schema the query names.</param>
    /// <param name="rootType">The root type its expression uses.</param>
    /// <param name="body">The lambda's body.</param>
    /// <param name="values">The declaration's lambda parameters after the root, comma separated, or <see langword="null"/> for none.</param>
    /// <returns>The source, whose declared field is on line 22.</returns>
    private static string DeclaredQuerySource(string clientNamespace, string rootType = "Query", string body = "null", string? values = null)
    {
        var parameters = values is null ? "x" : $"(x, {values})";
        var valueTypes = values is null ? string.Empty : string.Concat(Enumerable.Repeat(", object?", values.Split(',').Length));

        return $$"""
            using System.Linq.Expressions;
            using LinQL.Description;

            namespace Filtering
            {
                public class Query
                {
                    public string? Hero { get; set; }
                }

                public class Product
                {
                    public string? Name { get; set; }
                }
            }

            namespace Queries
            {
                public static class Declarations
                {
                    [LinQLQuery("{{clientNamespace}}")]
                    public static readonly Expression<System.Func<Filtering.{{rootType}}{{valueTypes}}, object?>> Value = {{parameters}} => {{body}};
                }
            }
            """;
    }

    private sealed class SdlFile(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
            => SourceText.From(content);
    }

    /// <summary>
    /// Supplies the additional-file metadata <c>LinQL.props</c> would add to the csproj.
    /// </summary>
    private sealed class NamespaceOptions(string? typesAs) : AnalyzerConfigOptionsProvider
    {
        private readonly TestOptions options = new(typesAs);

        public override AnalyzerConfigOptions GlobalOptions => this.options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => this.options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => this.options;
    }

    private sealed class TestOptions(string? typesAs) : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> values = Create(typesAs);

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => this.values.TryGetValue(key, out value);

        private static Dictionary<string, string> Create(string? typesAs)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_metadata.additionalfiles.LinQLClientNamespace"] = "Filtering",
            };

            if (typesAs is not null)
            {
                values["build_property.LinQLTypesAs"] = typesAs;
            }

            return values;
        }
    }
}
