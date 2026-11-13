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

    private const string PlainSdl = """
        schema {
          query: Query
        }

        type Query {
          hero: Character
        }

        type Character {
          name: String!
        }
        """;

    private static ImmutableArray<Diagnostic> Generate(string sdl, string? typesAs = null)
        => [.. Run(sdl, typesAs).Diagnostics];

    private static GeneratorDriverRunResult Run(string sdl, string? typesAs = null)
        => CSharpGeneratorDriver.Create(
            generators: [new ClientGenerator().AsSourceGenerator()],
            additionalTexts: [new SdlFile("SDLs/Filtering.graphql", sdl)],
            optionsProvider: new NamespaceOptions(typesAs))
        .RunGenerators(CSharpCompilation.Create("Filtering"))
        .GetRunResult();

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
