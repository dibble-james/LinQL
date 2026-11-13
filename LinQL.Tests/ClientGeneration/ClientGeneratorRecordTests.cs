namespace LinQL.Tests.ClientGeneration;

using LinQL.ClientGeneration;
using Xunit;

public class ClientGeneratorRecordTests
{
    private const string FilteringSdl = """
        schema {
          query: Query
        }

        interface SearchResult {
          name: String!
        }

        type Query {
          products(where: ProductFilterInput): [Product!]!
        }

        type Product implements SearchResult {
          name: String!
          price: Float!
        }

        input ProductFilterInput {
          name: StringOperationFilterInput
        }

        input StringOperationFilterInput {
          eq: String
          contains: String
        }
        """;

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

    [Fact]
    public void ClassesAreTheDefault()
    {
        var generated = Generate(PlainSdl);

        Assert.Contains("public partial class Character", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("partial record", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsAreGeneratedForObjectTypesInputsAndRoots()
    {
        var generated = Generate(FilteringSdl, LinQLTypesAs.Record);

        Assert.Contains("public partial record Product", generated, StringComparison.Ordinal);
        Assert.Contains("public partial record ProductFilterInput", generated, StringComparison.Ordinal);
        Assert.Contains("public partial record Query : RootType<Query>", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsLeaveInterfacesAndFilterBuildersAlone()
    {
        var generated = Generate(FilteringSdl, LinQLTypesAs.Record);

        // An interface stays an interface, and the fluent filter builder is not a schema type.
        Assert.Contains("public partial interface SearchResult", generated, StringComparison.Ordinal);
        Assert.Contains("public partial class ProductFilterInputBuilder", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordsInitialiseThroughInit()
    {
        var generated = Generate(FilteringSdl, LinQLTypesAs.Record);

        Assert.Contains("get; init;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("get; set;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassesKeepSettableProperties()
    {
        var generated = Generate(FilteringSdl);

        Assert.Contains("get; set;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("get; init;", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordModeChangesOnlyTheDeclarationAndAccessors()
        // Nothing else about a member changes between the modes: the difference is the declaration
        // keyword, the mutating accessor, and the record semantics (value equality, with, ToString)
        // that follow from them.
        => Assert.Equal(
            Generate(PlainSdl)
                .Replace("partial class", "partial record", StringComparison.Ordinal)
                .Replace("get; set;", "get; init;", StringComparison.Ordinal),
            Generate(PlainSdl, LinQLTypesAs.Record));

    private static string Generate(string sdl, LinQLTypesAs typesAs = LinQLTypesAs.Class)
        => ClientGenerator.Generate(new Microsoft.CodeAnalysis.SourceProductionContext(), "path", sdl, "Records", [], typesAs);
}
