namespace LinQL.Tests.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using HotChocolate.Language;
using LinQL.ClientGeneration;
using LinQL.Description;
using LinQL.Filters;
using LinQL.Translation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

/// <summary>
/// Checks that the runtime translator and the compile-time translator produce the same document model for the
/// vocabulary both of them support: fields, arguments and their variables, filters, <c>Includes</c> and the
/// <c>__typename</c> rule.
/// </summary>
/// <remarks>
/// Both paths build Hot Chocolate's document model and render it with the same writer, so comparing the rendered
/// models compares fields, arguments, variable definitions and selections rather than two independently formatted
/// strings. A spread is an inline fragment in both models, so a fragment is compared as a fragment rather than as a
/// field name.
/// The suite is scoped, as the design requires: the runtime path evaluates an argument by compiling it, so a
/// literal the caller wrote becomes a variable there, while a declared query writes a value the compiler saw into
/// the document. Every case here supplies its values as the query's parameters, where both paths declare the same
/// variables. A literal's spelling is each path's own, and is covered by that path's tests.
/// The declared side is read from source exactly as the generator reads it, with the fixture types below named the
/// way the SDL is; the runtime side is an ordinary expression over those types.
/// </remarks>
public class DeclaredQueryConformanceTests
{
    /// <summary>
    /// The schema both paths are written against.
    /// </summary>
    private const string Sdl = """
        schema {
          query: Query
          subscription: Subscription
        }

        type Query {
          products(where: ProductFilterInput, order: [ProductSortInput!]): [Product!]!
          characters(ids: [ID!]!): [Character!]!
          node: Node
          result: Result
          results: [Result!]!
        }

        type Subscription {
          renamed: [Character!]!
        }

        interface Node {
          id: ID!
        }

        type Character implements Node {
          id: ID!
          name: String!
        }

        type Film implements Node {
          id: ID!
          title: String!
        }

        union Result = Character | Film

        type Product {
          name: String!
          price: Float!
          brand: Brand
          tags: [Tag!]!
        }

        type Brand {
          name: String!
        }

        type Tag {
          label: String!
        }

        input ProductFilterInput {
          name: StringOperationFilterInput
          price: NumberOperationFilterInput
          brand: BrandFilterInput
          tags: TagListFilterInput
          and: [ProductFilterInput!]
          or: [ProductFilterInput!]
        }

        input StringOperationFilterInput {
          eq: String
          contains: String
          in: [String!]
        }

        input NumberOperationFilterInput {
          gt: Float
          lt: Float
        }

        input BrandFilterInput {
          name: StringOperationFilterInput
        }

        input TagFilterInput {
          label: StringOperationFilterInput
        }

        input TagListFilterInput {
          some: TagFilterInput
          all: TagFilterInput
          none: TagFilterInput
          any: Boolean
        }

        input ProductSortInput {
          name: SortEnumType
          price: SortEnumType
        }

        enum SortEnumType {
          ASC
          DESC
        }
        """;

    /// <summary>
    /// The framework the declaration's source resolves against, so its values are bound.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => MetadataReference.CreateFromFile(x)),
    ];

    [Fact]
    public void SelectsTheSameNestedFieldsAndTypeNames()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Products.Select(p => p.Tags.Select(t => new { t.Label })),
            "q.Products.Select(p => p.Tags.Select(t => new { t.Label }))");

    [Fact]
    public void PassesArgumentsAsTheSameVariables()
    {
        var ids = new[] { "1" };

        AssertConforms<ConformanceQuery, object>(
            x => x.ExecuteCharacters(ids).Select(c => new { c.Name }),
            "q.ExecuteCharacters(ids).Select(c => new { c.Name })",
            parameters: ["ids"],
            values: [ids]);
    }

    [Fact]
    public void WritesTheSameInterfaceFragment()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Node.On((ConformanceCharacter c) => c.Name),
            "q.Node.On((Character c) => c.Name)");

    [Fact]
    public void WritesTheSameUnionFragment()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Result.On((ConformanceFilm f) => f.Title),
            "q.Result.On((Film f) => f.Title)");

    [Fact]
    public void WritesTheSameFragmentForACast()
        => AssertConforms<ConformanceQuery, object>(
            x => ((ConformanceCharacter)x.Node!).Name,
            "((Character)q.Node).Name");

    [Fact]
    public void WritesTheSameFragmentForOfType()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Results.OfType<ConformanceCharacter>().Select(c => new { c.Name }),
            "q.Results.OfType<Character>().Select(c => new { c.Name })");

    [Fact]
    public void WritesTheSameFragmentForAnAsExpression()
        => AssertConforms<ConformanceQuery, object>(
            x => (x.Node as ConformanceFilm)!.Title,
            "(q.Node as Film).Title");

    [Fact]
    public void WritesTheSameSiblingFragmentsForAChain()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Result.On((ConformanceCharacter c) => c.Name).On((ConformanceFilm f) => f.Title),
            "q.Result.On((Character c) => c.Name).On((Film f) => f.Title)");

    [Fact]
    public void ExpandsTheSameScalarFields()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Products.SelectAll(),
            "q.Products.SelectAll()");

    [Fact]
    public void AddsTheSameIncludedFields()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Products.Includes(p => p.Tags.Select(t => t.Label)),
            "q.Products.Includes(p => p.Tags.Select(t => t.Label))");

    [Fact]
    public void BuildsTheSameFilterValue()
    {
        var min = 10f;

        AssertConforms<ConformanceQuery, object>(
            x => x.Products.Filter(f => f.Price(p => p.Gt(min))).Select(p => new { p.Name }),
            "q.Products.Filter(f => f.Price(p => p.Gt(min))).Select(p => new { p.Name })",
            parameters: ["min"],
            values: [min]);
    }

    [Fact]
    public void BuildsTheSameNestedFilterValue()
    {
        var term = "wid";

        AssertConforms<ConformanceQuery, object>(
            x => x.Products.Filter(f => f.Brand(b => b.Name(n => n.Contains(term)))).Select(p => new { p.Name }),
            "q.Products.Filter(f => f.Brand(b => b.Name(n => n.Contains(term)))).Select(p => new { p.Name })",
            parameters: ["term"],
            values: [term]);
    }

    [Fact]
    public void FoldsTheSameCombinators()
        => AssertConforms<ConformanceQuery, object>(
            x => x.Products.Filter(f => f.Name(n => n.Eq("widget")).Or(o => o.Name(n => n.Contains("wid")))).Select(p => new { p.Name }),
            "q.Products.Filter(f => f.Name(n => n.Eq(\"widget\")).Or(o => o.Name(n => n.Contains(\"wid\")))).Select(p => new { p.Name })");

    [Fact]
    public void BuildsTheSameFilterListValue()
    {
        var term = "widget";

        AssertConforms<ConformanceQuery, object>(
            x => x.Products.Filter(f => f.Name(n => n.In(new[] { term, "fixed" }))).Select(p => new { p.Name }),
            "q.Products.Filter(f => f.Name(n => n.In(new[] { term, \"fixed\" }))).Select(p => new { p.Name })",
            parameters: ["term"],
            values: [term]);
    }

    [Fact]
    public void PassesTheSameSortVariable()
    {
        var order = new[] { new ConformanceProductSortInput { Name = ConformanceSortEnum.ASC } };

        AssertConforms<ConformanceQuery, object>(
            x => x.Products.Sort(order).SelectAll(),
            "q.Products.Sort(order).SelectAll()",
            parameters: ["order"],
            values: [order]);
    }

    [Fact]
    public void OmitsTheRootTypeNameForASubscription()
        => AssertConforms<ConformanceSubscription, object>(
            x => x.Renamed.Select(c => new { c.Name }),
            "q.Renamed.Select(c => new { c.Name })",
            rootType: "Subscription");

    /// <summary>
    /// Asserts that both translators produce the same document and the same variables.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type the runtime expression is written against.</typeparam>
    /// <typeparam name="TData">The runtime expression's result type.</typeparam>
    /// <param name="runtime">The query as a runtime expression.</param>
    /// <param name="declared">The same query written as a declared query's lambda body.</param>
    /// <param name="rootType">The root the declaration is written against.</param>
    /// <param name="parameters">The declaration's parameters after the root.</param>
    /// <param name="values">The value each parameter is given, in the same order.</param>
    private static void AssertConforms<TRoot, TData>(
        Expression<Func<TRoot, TData>> runtime,
        string declared,
        string rootType = "Query",
        string[]? parameters = null,
        object?[]? values = null)
        where TRoot : RootType<TRoot>
    {
        var request = TranslationProvider.ToRequest(runtime, new LinQLOptions());

        var translation = Translate(declared, parameters, rootType);

        translation.Problems.Should().BeEmpty();
        translation.Document.Should().NotBeNull();

        // A value the runtime path compiled into a variable has the definition the declaration read from the SDL,
        // so the rendered model is where the two translators have to agree.
        Utf8GraphQLParser.Parse(request.Query).ToString().Should().Be(translation.Document.ToString());

        var inputs = (parameters ?? []).Zip(values ?? [], (parameter, value) => (parameter, value))
            .ToDictionary(x => x.parameter, x => x.value, StringComparer.Ordinal);

        // The compiled binder normalises each value the way LinQLQuery.Bind does before the query is sent.
        var bound = translation.Bindings.ToDictionary(
            x => x.Variable,
            x => FilterValue.Normalize(Evaluate(x.Value, inputs)),
            StringComparer.Ordinal);

        var translated = request.Expression.Variables.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);

        bound.Should().BeEquivalentTo(translated);
    }

    /// <summary>
    /// Reads a declared query's lambda the way the generator does.
    /// </summary>
    /// <param name="body">The lambda's body.</param>
    /// <param name="parameters">The declaration's parameters after the root.</param>
    /// <param name="rootType">The root type name the declaration uses.</param>
    /// <returns>What the translation produced.</returns>
    private static DeclaredQueryTranslation Translate(string body, string[]? parameters, string rootType)
    {
        var lambda = parameters is null || parameters.Length == 0
            ? "q"
            : $"(q, {string.Join(", ", parameters)})";

        var source = $$"""
            namespace Conformance
            {
                public static class Holder
                {
                    public static readonly System.Func<object, object> Query = {{lambda}} => {{body}};
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var bodyNode = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().First();
        var semantics = CSharpCompilation.Create("conformance", [tree], References).GetSemanticModel(tree);

        return DeclaredQueryTranslator.Translate(
            bodyNode,
            ClientGenerator.CreateModel(Sdl, "Conformance", LinQLTypesAs.Class),
            rootType,
            DeclaredValues.ReadAll(bodyNode, semantics));
    }

    /// <summary>
    /// Computes the value a generated binder would produce for one variable.
    /// </summary>
    /// <param name="value">The value the translation recorded.</param>
    /// <param name="inputs">Each parameter's value, by the name the declaration uses.</param>
    /// <returns>The value.</returns>
    private static object? Evaluate(DeclaredQueryBoundValue value, IReadOnlyDictionary<string, object?> inputs) => value switch
    {
        DeclaredQueryParameterValue parameter => inputs[parameter.Parameter],
        DeclaredQueryConstantValue constant => constant.Constant,
        DeclaredQueryListValue list => list.Items.Select(x => Evaluate(x, inputs)).ToArray(),
        DeclaredQueryFilterValue filter => EvaluateFilter(filter, inputs),
        _ => null,
    };

    /// <summary>
    /// Rebuilds a filter the way the emitted binder does: a combinator folds what came before it, and every other
    /// field is set under the name the schema gives it.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="inputs">Each parameter's value, by the name the declaration uses.</param>
    /// <returns>The filter value.</returns>
    private static FilterValue EvaluateFilter(DeclaredQueryFilterValue filter, IReadOnlyDictionary<string, object?> inputs)
    {
        var value = FilterValue.Create();

        foreach (var field in filter.Fields)
        {
            if (field.IsList && field.Value is DeclaredQueryFilterValue nested)
            {
                value.Combine(field.Name, EvaluateFilter(nested, inputs));
            }
            else
            {
                value.Set(field.Name, Evaluate(field.Value, inputs));
            }
        }

        return value;
    }

    /// <summary>
    /// The query root the runtime expressions above are written against.
    /// </summary>
    [OperationType]
    private sealed class ConformanceQuery : RootType<ConformanceQuery>
    {
        public ConformanceProduct[] Products { get; set; } = [];

        public IConformanceNode? Node { get; set; }

        public object? Result { get; set; }

        public object[] Results { get; set; } = [];

        [GraphQLOperation]
        [GraphQLField(Name = "characters")]
        public ConformanceCharacter[] ExecuteCharacters([GraphQLArgument(Name = "ids", GQLType = "[ID!]!")] string[] ids)
            => [];
    }

    /// <summary>
    /// The subscription root, so the one <c>__typename</c> exception is compared too.
    /// </summary>
    [OperationType(RootOperationType.Subscription)]
    private sealed class ConformanceSubscription : RootType<ConformanceSubscription>
    {
        public ConformanceCharacter[] Renamed { get; set; } = [];
    }

    [GraphQLType(Name = "Node")]
    internal interface IConformanceNode
    {
        public string Id { get; }
    }

    [GraphQLType(Name = "Character")]
    internal sealed class ConformanceCharacter : IConformanceNode
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    [GraphQLType(Name = "Film")]
    internal sealed class ConformanceFilm : IConformanceNode
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }

    internal sealed class ConformanceProduct : ICanHaveIncludes
    {
        public string Name { get; set; } = string.Empty;

        public float Price { get; set; }

        public ConformanceBrand? Brand { get; set; }

        public ConformanceTag[] Tags { get; set; } = [];
    }

    internal sealed class ConformanceBrand
    {
        public string Name { get; set; } = string.Empty;
    }

    internal sealed class ConformanceTag
    {
        public string Label { get; set; } = string.Empty;
    }

    internal sealed class ConformanceProductSortInput
    {
        public ConformanceSortEnum? Name { get; set; }

        public ConformanceSortEnum? Price { get; set; }
    }

    internal enum ConformanceSortEnum
    {
        ASC,
        DESC,
    }

    /// <summary>
    /// The filter and value builders the generator emits, hand-written here so a declaration's shape is written the
    /// way a generated client is.
    /// </summary>
    internal sealed class ConformanceProductFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public ConformanceProductFilterInputBuilder Name(Func<ConformanceStringFilterInputBuilder, ConformanceStringFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "price")]
        public ConformanceProductFilterInputBuilder Price(Func<ConformanceNumberFilterInputBuilder, ConformanceNumberFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "brand")]
        public ConformanceProductFilterInputBuilder Brand(Func<ConformanceBrandFilterInputBuilder, ConformanceBrandFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "tags")]
        public ConformanceProductFilterInputBuilder Tags(Func<ConformanceTagListFilterInputBuilder, ConformanceTagListFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "and", IsList = true)]
        public ConformanceProductFilterInputBuilder And(Func<ConformanceProductFilterInputBuilder, ConformanceProductFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "or", IsList = true)]
        public ConformanceProductFilterInputBuilder Or(Func<ConformanceProductFilterInputBuilder, ConformanceProductFilterInputBuilder> _) => this;
    }

    internal sealed class ConformanceStringFilterInputBuilder
    {
        [GraphQLFilterField(Name = "eq")]
        public ConformanceStringFilterInputBuilder Eq(string _) => this;

        [GraphQLFilterField(Name = "contains")]
        public ConformanceStringFilterInputBuilder Contains(string _) => this;

        [GraphQLFilterField(Name = "in")]
        public ConformanceStringFilterInputBuilder In(string[] _) => this;
    }

    internal sealed class ConformanceNumberFilterInputBuilder
    {
        [GraphQLFilterField(Name = "gt")]
        public ConformanceNumberFilterInputBuilder Gt(float _) => this;

        [GraphQLFilterField(Name = "lt")]
        public ConformanceNumberFilterInputBuilder Lt(float _) => this;
    }

    internal sealed class ConformanceBrandFilterInputBuilder
    {
        [GraphQLFilterField(Name = "name")]
        public ConformanceBrandFilterInputBuilder Name(Func<ConformanceStringFilterInputBuilder, ConformanceStringFilterInputBuilder> _) => this;
    }

    internal sealed class ConformanceTagFilterInputBuilder
    {
        [GraphQLFilterField(Name = "label")]
        public ConformanceTagFilterInputBuilder Label(Func<ConformanceStringFilterInputBuilder, ConformanceStringFilterInputBuilder> _) => this;
    }

    internal sealed class ConformanceTagListFilterInputBuilder
    {
        [GraphQLFilterField(Name = "some")]
        public ConformanceTagListFilterInputBuilder Some(Func<ConformanceTagFilterInputBuilder, ConformanceTagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "all")]
        public ConformanceTagListFilterInputBuilder All(Func<ConformanceTagFilterInputBuilder, ConformanceTagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "none")]
        public ConformanceTagListFilterInputBuilder None(Func<ConformanceTagFilterInputBuilder, ConformanceTagFilterInputBuilder> _) => this;

        [GraphQLFilterField(Name = "any")]
        public ConformanceTagListFilterInputBuilder Any(bool _) => this;
    }
}

/// <summary>
/// The extensions the generator emits for the conformance schema's arguments, hand-written here so the runtime path
/// can attach them without a generated client.
/// </summary>
internal static class ConformanceExtensions
{
    [GraphQLFilter(Argument = "where", GQLType = "ProductFilterInput")]
    public static DeclaredQueryConformanceTests.ConformanceProduct[] Filter(
        this DeclaredQueryConformanceTests.ConformanceProduct[] source,
        Expression<Func<DeclaredQueryConformanceTests.ConformanceProductFilterInputBuilder, DeclaredQueryConformanceTests.ConformanceProductFilterInputBuilder>> filter)
        => source;

    [GraphQLArgument(Name = "order", GQLType = "[ProductSortInput!]")]
    public static DeclaredQueryConformanceTests.ConformanceProduct[] Sort(
        this DeclaredQueryConformanceTests.ConformanceProduct[] source,
        DeclaredQueryConformanceTests.ConformanceProductSortInput[]? order = null)
        => source;
}
