#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using FluentAssertions;
using LinQL.Description;
using LinQL.Filters;
using LinQL.Translation;
using Xunit;

public partial class TranslationProviderTests
{
    [Fact]
    public void FilterScalarOperation()
        => this.TestFilter(x => x.FilterProducts(f => f.Name(n => n.Contains("widget"))).Select(p => new { p.Name }));

    [Fact]
    public void FilterMultipleOperations()
        => this.TestFilter(x => x.FilterProducts(f => f.Price(p => p.Gt(10).Lt(30))).Select(p => new { p.Name, p.Price }));

    [Fact]
    public void FilterNestedObject()
        => this.TestFilter(x => x.FilterProducts(f => f.Brand(b => b.Name(n => n.Eq("acme")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterSome()
        => this.TestFilter(x => x.FilterProducts(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAll()
        => this.TestFilter(x => x.FilterProducts(f => f.Tags(t => t.All(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterNone()
        => this.TestFilter(x => x.FilterProducts(f => f.Tags(t => t.None(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAny()
        => this.TestFilter(x => x.FilterProducts(f => f.Tags(t => t.Any(true))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAnd()
        => this.TestFilter(x => x.FilterProducts(f => f.Price(p => p.Gt(10)).And(a => a.Name(n => n.Eq("widget")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterOr()
        => this.TestFilter(x => x.FilterProducts(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterWithoutOperationsThrows()
    {
        Expression<Func<FilterRootType, object>> expression = x => x.FilterProducts(f => f).Select(p => new { p.Name });

        var act = () => TranslationProvider.ToRequest(expression, new LinQLOptions());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FilterFromInputObject()
        => this.TestPocoFilter(x => x.FilterProducts(new PocoProductFilterInput
        {
            Name = new PocoStringOperationFilterInput { Contains = "widget" },
            Price = new PocoNumberOperationFilterInput { Gt = 10 },
            Tags = new PocoTagListFilterInput { Some = new PocoTagFilterInput { Label = new PocoStringOperationFilterInput { Eq = "new" } } },
        }).Select(p => new { p.Name }));

    [Fact]
    public void FilterFromInputObjectWithValueTypeOperationThrows()
    {
        // A hand-written filter type with a non-nullable operation cannot express "not set", so sending
        // its default would silently filter on false instead of not filtering.
        Expression<Func<ValueTypeFilterRootType, object>> expression = x => x
            .FilterProducts(new ValueTypeFilterInput { Any = true })
            .Select(p => new { p.Name });

        var act = () => TranslationProvider.ToRequest(expression, new LinQLOptions());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*ValueTypeFilterInput.Any*Boolean?*");
    }

    [Fact]
    public void OnlyFilterValuesRejectValueTypeProperties()
    {
        // Forwarded arguments are not strict: a value the caller passed is always meaningful there, so a
        // non-nullable int on a sort or paging input is fine.
        FilterValue.Normalize(new { Count = 3 })
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["count"] = 3 });

        var act = () => FilterValue.From(new { Count = 3 });

        act.Should().Throw<InvalidOperationException>();
    }

    private void TestFilter(Expression<Func<FilterRootType, object>> expression)
        => this.SnapshotRequest(expression);

    private void TestPocoFilter(Expression<Func<PocoFilterRootType, object>> expression)
        => this.SnapshotRequest(expression);

    private void SnapshotRequest<TRoot, TData>(Expression<Func<TRoot, TData>> expression)
        where TRoot : RootType<TRoot>
    {
        var request = TranslationProvider.ToRequest(expression, new LinQLOptions());

        Snapshot.Match($"{request.Query}\n--- variables ---\n{JsonSerializer.Serialize(request.Variables)}");
    }

    [OperationType]
    private class FilterRootType : RootType<FilterRootType>
    {
        public FilterProduct[] Products { get; set; } = [];

        [GraphQLFilter(Field = "products", Argument = "where", GQLType = "ProductFilterInput")]
        public FilterProduct[] FilterProducts(Expression<Func<ProductFilterInputBuilder, ProductFilterInputBuilder>> filter)
            => IgnoreFilter(filter, this.Products);

        private static FilterProduct[] IgnoreFilter(Expression<Func<ProductFilterInputBuilder, ProductFilterInputBuilder>> _, FilterProduct[] products) => products;
    }

    [OperationType]
    private class PocoFilterRootType : RootType<PocoFilterRootType>
    {
        public FilterProduct[] Products { get; set; } = [];

        [GraphQLFilter(Field = "products", Argument = "where", GQLType = "ProductFilterInput")]
        public FilterProduct[] FilterProducts(PocoProductFilterInput _) => this.Products;
    }

    [OperationType]
    private class ValueTypeFilterRootType : RootType<ValueTypeFilterRootType>
    {
        public FilterProduct[] Products { get; set; } = [];

        [GraphQLFilter(Field = "products", Argument = "where", GQLType = "ProductFilterInput")]
        public FilterProduct[] FilterProducts(ValueTypeFilterInput _) => this.Products;
    }

    private sealed class ValueTypeFilterInput
    {
        public bool Any { get; set; }
    }

    private sealed class PocoProductFilterInput
    {
        public PocoProductFilterInput[]? And { get; set; }

        public PocoProductFilterInput[]? Or { get; set; }

        public PocoStringOperationFilterInput? Name { get; set; }

        public PocoNumberOperationFilterInput? Price { get; set; }

        public PocoTagListFilterInput? Tags { get; set; }
    }

    private sealed class PocoStringOperationFilterInput
    {
        public string? Eq { get; set; }

        public string? Contains { get; set; }
    }

    private sealed class PocoNumberOperationFilterInput
    {
        public decimal? Gt { get; set; }

        public decimal? Lt { get; set; }
    }

    private sealed class PocoTagFilterInput
    {
        public PocoStringOperationFilterInput? Label { get; set; }
    }

    private sealed class PocoTagListFilterInput
    {
        public PocoTagFilterInput? Some { get; set; }

        public PocoTagFilterInput? All { get; set; }

        public PocoTagFilterInput? None { get; set; }

        public bool? Any { get; set; }
    }

    private sealed class FilterProduct
    {
        public string Name { get; set; } = string.Empty;

        public float Price { get; set; }

        public FilterBrand? Brand { get; set; }

        public FilterTag[] Tags { get; set; } = [];
    }

    private sealed class FilterBrand
    {
        public string? Name { get; set; }
    }

    private sealed class FilterTag
    {
        public string Label { get; set; } = string.Empty;
    }

    private sealed class ProductFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public ProductFilterInputBuilder Name(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "price")]
        public ProductFilterInputBuilder Price(Func<NumberFilterInputBuilder, NumberFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "brand")]
        public ProductFilterInputBuilder Brand(Func<BrandFilterInputBuilder, BrandFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "tags")]
        public ProductFilterInputBuilder Tags(Func<TagListFilterInputBuilder, TagListFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "and", IsList = true)]
        public ProductFilterInputBuilder And(Func<ProductFilterInputBuilder, ProductFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "or", IsList = true)]
        public ProductFilterInputBuilder Or(Func<ProductFilterInputBuilder, ProductFilterInputBuilder> _) => this;
    }

    private sealed class StringFilterInputBuilder
    {
        [GraphQLFilterField(Name = "eq")]
        public StringFilterInputBuilder Eq(string _) => this;

        [GraphQLFilterField(Name = "contains")]
        public StringFilterInputBuilder Contains(string _) => this;
    }

    private sealed class NumberFilterInputBuilder
    {
        [GraphQLFilterField(Name = "gt")]
        public NumberFilterInputBuilder Gt(decimal _) => this;

        [GraphQLFilterField(Name = "lt")]
        public NumberFilterInputBuilder Lt(decimal _) => this;
    }

    private sealed class BrandFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public BrandFilterInputBuilder Name(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;
    }

    private sealed class TagFilterInputBuilder
    {
        [GraphQLFilterField(Name = "label")]
        public TagFilterInputBuilder Label(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;
    }

    private sealed class TagListFilterInputBuilder
    {
        [GraphQLFilterField(Name = "some")]
        public TagListFilterInputBuilder Some(Func<TagFilterInputBuilder, TagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "all")]
        public TagListFilterInputBuilder All(Func<TagFilterInputBuilder, TagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "none")]
        public TagListFilterInputBuilder None(Func<TagFilterInputBuilder, TagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "any")]
        public TagListFilterInputBuilder Any(bool _) => this;
    }
}
