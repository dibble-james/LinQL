namespace LinQL.Generated.Tests;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using LinQL;
using LinQL.Generated.Tests.Filtering;
using LinQL.Translation;
using Client = LinQL.Generated.Tests.Filtering;

/// <summary>
/// Translates expressions built from the generated client, so the builder methods and field extensions
/// the generator emits are exercised rather than the hand-written fixtures in LinQL.Tests.
/// </summary>
public class GeneratedFilterTests
{
    private static readonly string[] WidgetNames = ["widget", "gadget"];

    private static readonly Client.ProductSortInput[] PriceDescending = [new() { Price = Client.SortEnumType.DESC }];

    [Fact]
    public void ScalarOperation()
        => Translate(q => q
            .Products
            .Filter(f => f.Name(n => n.Contains("widget")).Price(p => p.Gt(10)))
            .Select(p => new { p.Id, p.Name }));

    [Fact]
    public void OrFoldsIntoTheFirstBranch()
        => Translate(q => q
            .Products
            .Filter(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget"))))
            .Select(p => new { p.Name }));

    [Fact]
    public void CollectionQuantifier()
        => Translate(q => q
            .Products
            .Filter(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new")))))
            .Select(p => new { p.Name }));

    [Fact]
    public void AndFoldsIntoTheFirstBranch()
        => Translate(q => q
            .Products
            .Filter(f => f.Name(n => n.Contains("w")).And(a => a.Price(p => p.Lt(5))))
            .Select(p => new { p.Name }));

    [Fact]
    public void NoneWithNegatedOperation()
        => Translate(q => q
            .Products
            .Filter(f => f.Tags(t => t.None(s => s.Label(l => l.Ncontains("new")))))
            .Select(p => new { p.Name }));

    [Fact]
    public void QuantifierValue()
        => Translate(q => q
            .Products
            .Filter(f => f.Tags(t => t.Any(false)))
            .Select(p => new { p.Name }));

    [Fact]
    public void ScalarListOperation()
        => Translate(q => q
            .Products
            .Filter(f => f.Name(n => n.In(WidgetNames)))
            .Select(p => new { p.Name }));

    [Fact]
    public void ArgumentExtensionAddsTheValue()
        => Translate(q => q
            .Products
            .Filter(f => f.Name(n => n.Eq("widget")))
            .In(1)
            .Select(p => new { p.Name }));

    [Fact]
    public void RequiredArgumentExtension()
        => Translate(q => q
            .Featured
            .First(5)
            .Filter(f => f.Price(p => p.Gt(10)))
            .Select(p => new { p.Name }));

    [Fact]
    public void InputObjectArgumentExtension()
        => Translate(q => q
            .Featured
            .First(3)
            .Filter(f => f.Price(p => p.Gt(10)))
            .Order(PriceDescending)
            .Select(p => new { p.Name }));

    [Fact]
    public void SortAliasForOrderArgument()
        => Translate(q => q
            .Featured
            .First(3)
            .Sort(PriceDescending)
            .Select(p => new { p.Name }));

    [Fact]
    public void InterfaceFieldFilter()
        => Translate(q => q
            .Results
            .Select(r => new
            {
                r.Id,
                Related = r.Related.Filter(f => f.Name(n => n.Eq("widget"))).Select(p => new { p.Name }),
            }));

    [Fact]
    public void ScalarListOperationOnQueryArgument()
        => Translate(q => q.ExecuteProducts(null, 1).Select(p => new { p.Name }));

    private static void Translate(Expression<Func<Client.Query, object>> expression)
    {
        var request = TranslationProvider.ToRequest(expression, new LinQLOptions());

        Snapshot.Match($"{request.Query}\n--- variables ---\n{JsonSerializer.Serialize(request.Variables)}");
    }
}
