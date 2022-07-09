namespace LinQL.Tests.ClientGeneration;

using System.Text;
using LinQL.ClientGeneration;
using Xunit;

public class ClientGeneratorTests
{
    [Fact]
    public void StarWarsClient()
        => Snapshot.Match(ClientGenerator.Generate(
            Encoding.UTF8.GetString(SDLs.StarWars),
            "StarWarsGraph",
            "StarWars"));

    [Fact]
    public void ShiftshareClient()
        => Snapshot.Match(ClientGenerator.Generate(
            Encoding.UTF8.GetString(SDLs.Shiftshare),
            "ShiftshareGraph",
            "Shiftshare.Graph"));
}
