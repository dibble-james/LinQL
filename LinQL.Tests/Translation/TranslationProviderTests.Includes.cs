#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

using System;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using LinQL.Description;
using LinQL.Translation;
using Xunit;

public partial class TranslationProviderTests
{
    [Fact]
    public void IncludesField()
        => this.Test<IncludesRootType, object>(x => x.Operation.Includes(o => o.GetNumber("123")));

    [Fact]
    public void IncludesNestedOperation()
        => this.Test<IncludesRootType, object>(x => x.Operation.GetNumber("1").Includes(o => o.GetNumber("123")));

    [Fact]
    public void IncludesCollectionElement()
        => this.Test<IncludesRootType, IncludesOperationType[]>(x => x.Operations.Includes(o => o.GetNumber("123")));

    [Fact]
    public void IncludesInsideSelectMatchesTheChainedForm()
    {
        // The include is the Select's selection, so the two spellings must produce the same document.
        var chained = TranslationProvider.ToRequest(
            (Expression<Func<FilterRootType, object>>)(x => x.Products.Includes(p => p.Tags.Select(t => t.Label))),
            new LinQLOptions());

        var insideSelect = TranslationProvider.ToRequest(
            (Expression<Func<FilterRootType, object>>)(x => x.Products.Select(p => p.Includes(t => t.Tags.Select(tag => tag.Label)))),
            new LinQLOptions());

        insideSelect.Query.Should().Be(chained.Query);
    }

    [Fact]
    public void IncludesInsideOnLambdaMatchesTheSpreadSelection()
    {
        // The receiver is the On lambda's parameter, so the include joins the ... on block it is scoped to.
        var spread = TranslationProvider.ToRequest(
            (Expression<Func<InterfaceRootType, object>>)(x => x.SimpleType.On((SomeOtherSimpleType y) => y.Float)),
            new LinQLOptions());

        var withInclude = TranslationProvider.ToRequest(
            (Expression<Func<InterfaceRootType, object>>)(x => x.SimpleType.On((SomeOtherSimpleType y) => y.Includes(z => z.Float))),
            new LinQLOptions());

        withInclude.Query.Should().Be(spread.Query);
    }

    [Fact]
    public void IncludesComposesWithFilterRegardlessOfOrder()
    {
        // Each call attaches to the field its receiver resolves to, so the document does not depend on
        // whether the include comes before or after the filter and the argument extension. First and Sort
        // are the same GraphQLArgument mechanism as In, so this covers their ordering too.
        var includeFirst = TranslationProvider.ToRequest(
            (Expression<Func<FilterRootType, object>>)(x => x
                .Products
                .Includes(p => p.Tags.Select(t => t.Label))
                .Filter(f => f.Name(n => n.Eq("widget")))
                .In(3)
                .Select(p => new { p.Name })),
            new LinQLOptions());

        var filterFirst = TranslationProvider.ToRequest(
            (Expression<Func<FilterRootType, object>>)(x => x
                .Products
                .Filter(f => f.Name(n => n.Eq("widget")))
                .In(3)
                .Includes(p => p.Tags.Select(t => t.Label))
                .Select(p => new { p.Name })),
            new LinQLOptions());

        filterFirst.Query.Should().Be(includeFirst.Query);
    }
}

[OperationType]
internal sealed class IncludesRootType : RootType<IncludesRootType>
{
    public IncludesOperationType Operation { get; set; } = new();

    public IncludesOperationType[] Operations { get; set; } = [];
}

internal sealed class IncludesOperationType : ICanHaveIncludes
{
    public int Number { get; set; }

    public string? Text { get; set; }

    [GraphQLOperation]
    public IncludesOperationType GetNumber([GraphQLArgument(GQLType = "String!")] string value) => new() { Text = value };
}
