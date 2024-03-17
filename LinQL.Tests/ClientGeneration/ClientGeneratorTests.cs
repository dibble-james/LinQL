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

    [Fact]
    public void ShiftshareClient()
        => Snapshot.Match(ClientGenerator.Generate(
            new Microsoft.CodeAnalysis.SourceProductionContext(),
            "path",
            Encoding.UTF8.GetString(SDLs.Shiftshare) + Encoding.UTF8.GetString(SDLs.Shiftshare_extensions),
            "Shiftshare.Graph",
            ExtraUsings));
}
