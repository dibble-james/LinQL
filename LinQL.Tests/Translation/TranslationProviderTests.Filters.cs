#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using LinQL.Description;
using LinQL.Filters;
using LinQL.Translation;
using Xunit;

public partial class TranslationProviderTests
{
    [Fact]
    public void FilterScalarOperation()
        => this.TestFilter(x => x.Products.Filter(f => f.Name(n => n.Contains("widget"))).Select(p => new { p.Name }));

    [Fact]
    public void FilterMultipleOperations()
        => this.TestFilter(x => x.Products.Filter(f => f.Price(p => p.Gt(10).Lt(30))).Select(p => new { p.Name, p.Price }));

    [Fact]
    public void FilterNestedObject()
        => this.TestFilter(x => x.Products.Filter(f => f.Brand(b => b.Name(n => n.Eq("acme")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterSome()
        => this.TestFilter(x => x.Products.Filter(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAll()
        => this.TestFilter(x => x.Products.Filter(f => f.Tags(t => t.All(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterNone()
        => this.TestFilter(x => x.Products.Filter(f => f.Tags(t => t.None(s => s.Label(l => l.Eq("new"))))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAny()
        => this.TestFilter(x => x.Products.Filter(f => f.Tags(t => t.Any(true))).Select(p => new { p.Name }));

    [Fact]
    public void FilterAnd()
        => this.TestFilter(x => x.Products.Filter(f => f.Price(p => p.Gt(10)).And(a => a.Name(n => n.Eq("widget")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterOr()
        => this.TestFilter(x => x.Products.Filter(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget")))).Select(p => new { p.Name }));

    [Fact]
    public void FilterWithoutOperationsThrows()
    {
        Expression<Func<FilterRootType, object>> expression = x => x.Products.Filter(f => f).Select(p => new { p.Name });

        var act = () => TranslationProvider.ToRequest(expression, new LinQLOptions());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FilterFromInputObject()
        => this.TestFilter(x => x.Products.Filter(new PocoProductFilterInput
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
        Expression<Func<FilterRootType, object>> expression = x => x
            .Products
            .Filter(new ValueTypeFilterInput { Any = true })
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

    [Fact]
    public void ArgumentsNormaliseDictionariesAsObjects()
    {
        // A dictionary is usually typed more tightly than Dictionary<string, object?>, and a non-generic one
        // matches no generic arm at all; either would fall through to the enumerable arm and serialise as
        // [{"key": "filter", "value": "name"}] instead of an object. Keys are the caller's wire names, so
        // they are passed through as written.
        FilterValue.Normalize(new Dictionary<string, string> { ["filter"] = "name", ["value"] = "widget" })
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["filter"] = "name", ["value"] = "widget" });

        FilterValue.Normalize(new Hashtable { ["count"] = 3 })
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["count"] = 3 });

        // A read-only dictionary such as ImmutableDictionary implements no non-generic IDictionary, so it
        // is recognised by its IReadOnlyDictionary<string, T> interface instead.
        FilterValue.Normalize(ImmutableDictionary.CreateRange(new Dictionary<string, string> { ["name"] = "widget" }))
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["name"] = "widget" });
    }

    [Fact]
    public void ArgumentsHonourJsonPropertyName()
        => FilterValue.Normalize(new RenamedArgument { Text = "widget" })
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["query"] = "widget" });

    [Fact]
    public void ArgumentsHonourJsonIgnore()
        => FilterValue.Normalize(new PartlyIgnoredArgument { Text = "widget", Present = "kept" })
            .Should().BeEquivalentTo(new Dictionary<string, object?> { ["text"] = "widget", ["present"] = "kept" });

    [Fact]
    public void ArgumentsHonourCustomConverters()
    {
        // The converter decides the shape, so reflection must not: here an object writes itself as a string.
        var normalized = FilterValue.Normalize(new { Converted = new ConvertedArgument { Text = "widget" }, Plain = "as-is" });

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(normalized));

        json.RootElement.GetProperty("converted").GetString().Should().Be("WIDGET");
        json.RootElement.GetProperty("plain").GetString().Should().Be("as-is");
    }

    [Fact]
    public void ArgumentsPassJsonElementsThrough()
    {
        // A JsonElement is already wire-shaped: reflecting over it would send `valueKind`/`rawText`
        // instead of the JSON it holds.
        using var document = JsonDocument.Parse("""{"a":1}""");

        FilterValue.Normalize(document.RootElement)
            .Should().Be(document.RootElement);

        JsonSerializer.Serialize(FilterValue.Normalize(document))
            .Should().Be("""{"a":1}""");
    }

    [Fact]
    public void ArgumentExtensionAddsTheValue()
        => this.TestFilter(x => x.Products.In(3).Select(p => new { p.Name }));

    private void TestFilter(Expression<Func<FilterRootType, object>> expression)
    {
        var request = TranslationProvider.ToRequest(expression, new LinQLOptions());

        Snapshot.Match($"{request.Query}\n--- variables ---\n{JsonSerializer.Serialize(request.Variables)}");
    }

    [OperationType]
    private class FilterRootType : RootType<FilterRootType>
    {
        public FilterProduct[] Products { get; set; } = [];

        public FilterProduct[] Recommended { get; set; } = [];
    }

    internal sealed class ValueTypeFilterInput
    {
        public bool Any { get; set; }
    }

    private sealed class RenamedArgument
    {
        [JsonPropertyName("query")]
        public string? Text { get; set; }
    }

    [JsonConverter(typeof(UpperCaseConverter))]
    private sealed class ConvertedArgument
    {
        public string Text { get; set; } = string.Empty;
    }

    private sealed class UpperCaseConverter : JsonConverter<ConvertedArgument>
    {
        public override ConvertedArgument Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new() { Text = reader.GetString() ?? string.Empty };

        public override void Write(Utf8JsonWriter writer, ConvertedArgument value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.Text.ToUpperInvariant());
    }

    private sealed class PartlyIgnoredArgument
    {
        public string? Text { get; set; }

        [JsonIgnore]
        public string? Hidden { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Present { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Missing { get; set; }
    }

    internal sealed class PocoProductFilterInput
    {
        public PocoProductFilterInput[]? And { get; set; }

        public PocoProductFilterInput[]? Or { get; set; }

        public PocoStringOperationFilterInput? Name { get; set; }

        public PocoNumberOperationFilterInput? Price { get; set; }

        public PocoTagListFilterInput? Tags { get; set; }
    }

    internal sealed class PocoStringOperationFilterInput
    {
        public string? Eq { get; set; }

        public string? Contains { get; set; }
    }

    internal sealed class PocoNumberOperationFilterInput
    {
        public decimal? Gt { get; set; }

        public decimal? Lt { get; set; }
    }

    internal sealed class PocoTagFilterInput
    {
        public PocoStringOperationFilterInput? Label { get; set; }
    }

    internal sealed class PocoTagListFilterInput
    {
        public PocoTagFilterInput? Some { get; set; }

        public PocoTagFilterInput? All { get; set; }

        public PocoTagFilterInput? None { get; set; }

        public bool? Any { get; set; }
    }

    internal sealed class FilterProduct
    {
        public string Name { get; set; } = string.Empty;

        public float Price { get; set; }

        public FilterBrand? Brand { get; set; }

        public FilterTag[] Tags { get; set; } = [];
    }

    internal sealed class FilterBrand
    {
        public string? Name { get; set; }
    }

    internal sealed class FilterTag
    {
        public string Label { get; set; } = string.Empty;
    }

    internal sealed class ProductFilterInputBuilder
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

    internal sealed class StringFilterInputBuilder
    {
        [GraphQLFilterField(Name = "eq")]
        public StringFilterInputBuilder Eq(string _) => this;

        [GraphQLFilterField(Name = "contains")]
        public StringFilterInputBuilder Contains(string _) => this;
    }

    internal sealed class NumberFilterInputBuilder
    {
        [GraphQLFilterField(Name = "gt")]
        public NumberFilterInputBuilder Gt(decimal _) => this;

        [GraphQLFilterField(Name = "lt")]
        public NumberFilterInputBuilder Lt(decimal _) => this;
    }

    internal sealed class BrandFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public BrandFilterInputBuilder Name(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;
    }

    internal sealed class TagFilterInputBuilder
    {
        [GraphQLFilterField(Name = "label")]
        public TagFilterInputBuilder Label(Func<StringFilterInputBuilder, StringFilterInputBuilder> _) => this;
    }

    internal sealed class TagListFilterInputBuilder
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

/// <summary>
/// The extensions the generator emits, hand-written here so the translator is covered without a client.
/// </summary>
internal static class FilterExtensions
{
    [GraphQLFilter(Argument = "where", GQLType = "ProductFilterInput")]
    public static TranslationProviderTests.FilterProduct[] Filter(this TranslationProviderTests.FilterProduct[] source, Expression<Func<TranslationProviderTests.ProductFilterInputBuilder, TranslationProviderTests.ProductFilterInputBuilder>> filter)
        => source;

    [GraphQLFilter(Argument = "where", GQLType = "ProductFilterInput")]
    public static TranslationProviderTests.FilterProduct[] Filter(this TranslationProviderTests.FilterProduct[] source, TranslationProviderTests.PocoProductFilterInput filter)
        => source;

    [GraphQLFilter(Argument = "where", GQLType = "ProductFilterInput")]
    public static TranslationProviderTests.FilterProduct[] Filter(this TranslationProviderTests.FilterProduct[] source, TranslationProviderTests.ValueTypeFilterInput filter)
        => source;

    [GraphQLArgument(Name = "in", GQLType = "Int")]
    public static TranslationProviderTests.FilterProduct[] In(this TranslationProviderTests.FilterProduct[] source, int? @in = null)
        => source;
}
