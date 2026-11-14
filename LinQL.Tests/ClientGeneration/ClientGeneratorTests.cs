namespace LinQL.Tests.ClientGeneration;

using System.Text;
using LinQL.ClientGeneration;
using Xunit;

public class ClientGeneratorTests
{
    [Fact]
    public void StarWarsClient()
        => Snapshot.Match(ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            Encoding.UTF8.GetString(SDLs.StarWars),
            "StarWars",
            []));

    private static readonly string[] ExtraUsings = ["NodaTime"];

    private const string FilteringSdl = """
        schema {
          query: Query
        }

        type Query {
          products(where: ProductFilterInput, in: Int): [Product!]!
        }

        type Product {
          name: String!
          price: Float!
          tags: [Tag!]!
        }

        type Tag {
          label: String!
        }

        input ProductFilterInput {
          and: [ProductFilterInput!]
          or: [ProductFilterInput!]
          name: StringOperationFilterInput
          price: NumberOperationFilterInput
          tags: ListFilterInputTypeOfTagFilterInput
        }

        input StringOperationFilterInput {
          eq: String
          contains: String
        }

        input NumberOperationFilterInput {
          eq: Float
          gt: Float
          lt: Float
        }

        input TagFilterInput {
          label: StringOperationFilterInput
        }

        input ListFilterInputTypeOfTagFilterInput {
          some: TagFilterInput
          all: TagFilterInput
          none: TagFilterInput
          any: Boolean
        }
        """;

    [Fact]
    public void UnionTypesDoNotImplementCanHaveIncludes()
    {
        // A union has no fields of its own — its selection is fragments only — so Includes has nothing
        // to add and the marker is left off, which makes it a compile error on a union receiver.
        var source = ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            UnionSdl,
            "Unions",
            []);

        Assert.Contains("public partial interface SearchResult", source, System.StringComparison.Ordinal);
        Assert.DoesNotContain("SearchResult : ICanHaveIncludes", source, System.StringComparison.Ordinal);
        Assert.Contains("public partial class Product : ICanHaveIncludes", source, System.StringComparison.Ordinal);
    }

    private const string UnionSdl = """
        schema {
          query: Query
        }

        type Query {
          search: [SearchResult!]!
        }

        union SearchResult = Product | Tag

        type Product {
          name: String!
        }

        type Tag {
          label: String!
        }
        """;

    [Fact]
    public void FilteringClient()
        => Snapshot.Match(ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            FilteringSdl,
            "Filtering",
            []));

    [Fact]
    public void InterfaceArgumentsClient()
        => Snapshot.Match(ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            Encoding.UTF8.GetString(SDLs.InterfaceArguments),
            "InterfaceArguments",
            []));

    [Fact]
    public void ShiftshareClient()
        => Snapshot.Match(ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            Encoding.UTF8.GetString(SDLs.Shiftshare) + Encoding.UTF8.GetString(SDLs.Shiftshare_extensions),
            "Shiftshare.Graph",
            ExtraUsings));
}
