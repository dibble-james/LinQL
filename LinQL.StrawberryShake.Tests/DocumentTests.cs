namespace LinQL.StrawberryShake.Tests;

using System.Text;
using FluentAssertions;
using LinQL.Description;
using global::StrawberryShake;
using Xunit;

public class DocumentTests
{
    [Fact]
    public void NormalizesAnonymousQuery()
    {
        var (query, name) = LinQLDocument.Normalize("query {\n  number\n}");

        name.Should().Be("linql");
        query.Should().StartWith("query linql {");
    }

    [Fact]
    public void KeepsNamedQuery()
    {
        var (query, name) = LinQLDocument.Normalize("query linql(\n  $var1: ID!\n)\n {\n  film\n}");

        name.Should().Be("linql");
        query.Should().StartWith("query linql(");
    }

    [Theory]
    [InlineData(RootOperationType.Query, OperationKind.Query)]
    [InlineData(RootOperationType.Mutation, OperationKind.Mutation)]
    [InlineData(RootOperationType.Subscription, OperationKind.Subscription)]
    public void MapsOperationKind(RootOperationType type, OperationKind expected)
    {
        var document = new LinQLDocument(type, "linql", "query linql { number }");

        document.Kind.Should().Be(expected);
    }

    [Fact]
    public void HashesTheBody()
    {
        var document = new LinQLDocument(RootOperationType.Query, "linql", "query linql { number }");

        document.Hash.Algorithm.Should().Be("sha256");
        document.Hash.Value.Should().NotBeNullOrEmpty();
        Encoding.UTF8.GetString(document.Body).Should().Be("query linql { number }");
    }
}
