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

internal sealed class IncludesOperationType
{
    public int Number { get; set; }

    public string? Text { get; set; }

    [GraphQLOperation]
    public IncludesOperationType GetNumber([GraphQLArgument(GQLType = "String!")] string value) => new() { Text = value };
}

/// <summary>
/// The <c>Includes</c> extensions the generator emits, written by hand here so the translator is
/// covered without a client.
/// </summary>
internal static class IncludesExtensions
{
    [GraphQLIncludes]
    public static IncludesOperationType Includes(this IncludesOperationType source, Expression<Func<IncludesOperationType, object>> include)
        => source;

    [GraphQLIncludes]
    public static IncludesOperationType[] Includes(this IncludesOperationType[] source, Expression<Func<IncludesOperationType, object>> include)
        => source;

    [GraphQLIncludes]
    public static TranslationProviderTests.FilterProduct[] Includes(this TranslationProviderTests.FilterProduct[] source, Expression<Func<TranslationProviderTests.FilterProduct, object>> include)
        => source;
}
