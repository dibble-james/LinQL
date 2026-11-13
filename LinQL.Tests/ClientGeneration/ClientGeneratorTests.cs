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
