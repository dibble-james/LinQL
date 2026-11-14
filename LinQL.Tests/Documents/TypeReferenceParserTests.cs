namespace LinQL.Tests.Documents;

using FluentAssertions;
using LinQL.Documents;
using Xunit;

public class TypeReferenceParserTests
{
    [Theory]
    [InlineData("String")]
    [InlineData("String!")]
    [InlineData("[Film!]!")]
    [InlineData("[[Int]]")]
    public void ParsesTypeReferences(string type)
        => TypeReferenceParser.Parse(type).ToString().Should().Be(type);

    [Fact]
    public void RejectsMalformedTypeReferences()
    {
        var act = () => TypeReferenceParser.Parse("[Film!");

        act.Should().Throw<FormatException>();
    }
}
