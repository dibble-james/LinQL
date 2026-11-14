namespace LinQL.Tests;

using LinQL.Description;

public class LinQLOptionsTests
{
    [Fact]
    public void ScalarsStartWithTheNativeScalars()
    {
        var options = new LinQLOptions();

        options.Scalars.Select(x => x.Name).Should().BeEquivalentTo("Int", "Float", "String", "Id", "Boolean");
    }

    [Fact]
    public void ScalarsAreNotSharedBetweenInstances()
    {
        var configured = new LinQLOptions();
        configured.Scalars.Add(new Scalar("Widget", "Widget"));

        var other = new LinQLOptions();

        other.Scalars.Should().NotContain(x => x.Name == "Widget");
    }
}
