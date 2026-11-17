namespace LinQL.Tests;

using System.Linq.Expressions;
using LinQL.Description;

public class PersistedQueryTests
{
    private static readonly Expression<Func<DeclaredQueryRoot, string>> Name = q => q.Name;

    [Fact]
    public void ComputesTheLowercaseHexSha256OfTheDocument()
        => PersistedQueryId.Compute("{__typename}")
            .Should().Be("ecf4edb46db40b5132295c0291d62fb65d6759a9eedfa4d5d612dd5ec54a6b38");

    [Fact]
    public void RejectsANullDocument()
        => FluentActions.Invoking(() => PersistedQueryId.Compute(null!))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void BindComputesTheIdWhenTheBinderDoesNotSupplyOne()
    {
        var query = LinQLQuery<DeclaredQueryRoot, string>.Bind(
            "query linql { __typename name }",
            RootOperationType.Query,
            Name);

        query.Id.Should().Be(PersistedQueryId.Compute(query.Document));
    }

    [Fact]
    public void BindKeepsTheIdTheBinderSupplies()
    {
        var query = LinQLQuery<DeclaredQueryRoot, string>.Bind(
            "query linql { __typename name }",
            "generated-id",
            RootOperationType.Query,
            Name);

        query.Id.Should().Be("generated-id");
    }
}
