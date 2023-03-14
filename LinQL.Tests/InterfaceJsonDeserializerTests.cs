namespace LinQL.Tests;

using System.Text.Json;

public class InterfaceJsonDeserializerTests
{
    private readonly JsonSerializerOptions options;

    public InterfaceJsonDeserializerTests()
    {
        this.options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        this.options.Converters.Add(
            new InterfaceJsonDeserializer<InterfaceType>(new[]
            {
                typeof(ConcreteClass1),
                typeof(ConcreteClass2),
            }));
    }

    [Fact]
    public void DeserializeInterface()
    {
        var result = JsonSerializer.Deserialize<Data>(JsonFiles.Interfaces, this.options);

        result!.Input.Should().BeOfType<ConcreteClass1>().Which.Text.Should().Be("I am a concrete type");
        result!.Output.Should().BeOfType<ConcreteClass2>().Which.Number.Should().Be(87);
    }

    [Fact]
    public void DeserializeArrayOfInterface()
    {
        var result = JsonSerializer.Deserialize<InterfaceType[]>(JsonFiles.ArrayOfInterfaces, this.options);

        result!.First().Should().BeOfType<ConcreteClass1>().Which.Text.Should().Be("I am a concrete type");
        result!.Last().Should().BeOfType<ConcreteClass2>().Which.Number.Should().Be(87);
    }

    private sealed class Data
    {
        public InterfaceType Input { get; set; }

        public InterfaceType Output { get; set; }
    }

    private interface InterfaceType { }

    private sealed class ConcreteClass1 : InterfaceType
    {
        public string Text { get; set; }
    }

    private sealed class ConcreteClass2 : InterfaceType
    {
        public int Number { get; set; }
    }
}
