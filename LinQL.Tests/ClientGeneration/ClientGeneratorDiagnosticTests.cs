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

    [Fact]
    public void MissingFilterInputTypeIsReported()
    {
        var diagnostics = Generate(MissingFilterInputSdl);

        var diagnostic = Assert.Single(diagnostics, x => x.Id == "LINQLGEN05");

        Assert.Contains("'where'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'products'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'ProductFilterInput'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredFilterInputTypeIsNotReported()
        => Assert.DoesNotContain(Generate(DeclaredFilterInputSdl), x => x.Id == "LINQLGEN05");

    private static ImmutableArray<Diagnostic> Generate(string sdl)
    {
        var driver = CSharpGeneratorDriver.Create(
            generators: [new ClientGenerator().AsSourceGenerator()],
            additionalTexts: [new SdlFile("SDLs/Filtering.graphql", sdl)],
            optionsProvider: new NamespaceOptions());

        return [.. driver
            .RunGenerators(CSharpCompilation.Create("Filtering"))
            .GetRunResult()
            .Diagnostics];
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
    private sealed class NamespaceOptions : AnalyzerConfigOptionsProvider
    {
        private static readonly TestOptions Options = new();

        public override AnalyzerConfigOptions GlobalOptions => Options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options;
    }

    private sealed class TestOptions : AnalyzerConfigOptions
    {
        private static readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["build_metadata.additionalfiles.LinQLClientNamespace"] = "Filtering",
        };

        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            => Values.TryGetValue(key, out value);
    }
}
