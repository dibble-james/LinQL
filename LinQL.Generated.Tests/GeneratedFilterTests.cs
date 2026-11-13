namespace LinQL.Generated.Tests;

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using LinQL;
using LinQL.Translation;
using Client = LinQL.Generated.Tests.Filtering;

/// <summary>
/// Translates expressions built from the generated client, so the builder methods the generator emits
/// are exercised rather than the hand-written fixtures in LinQL.Tests.
/// </summary>
public class GeneratedFilterTests
{
    private static readonly string[] WidgetNames = ["widget", "gadget"];

    [Fact]
    public void ScalarOperation()
        => Translate(q => q
            .FilterProducts(f => f.Name(n => n.Contains("widget")).Price(p => p.Gt(10)))
            .Select(p => new { p.Id, p.Name }));

    [Fact]
    public void OrFoldsIntoTheFirstBranch()
        => Translate(q => q
            .FilterProducts(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget"))))
            .Select(p => new { p.Name }));

    [Fact]
    public void CollectionQuantifier()
        => Translate(q => q
            .FilterProducts(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new")))))
            .Select(p => new { p.Name }));

    [Fact]
    public void AndFoldsIntoTheFirstBranch()
        => Translate(q => q
            .FilterProducts(f => f.Name(n => n.Contains("w")).And(a => a.Price(p => p.Lt(5))))
            .Select(p => new { p.Name }));

    [Fact]
    public void NoneWithNegatedOperation()
        => Translate(q => q
            .FilterProducts(f => f.Tags(t => t.None(s => s.Label(l => l.Ncontains("new")))))
            .Select(p => new { p.Name }));

    [Fact]
    public void QuantifierValue()
        => Translate(q => q
            .FilterProducts(f => f.Tags(t => t.Any(false)))
            .Select(p => new { p.Name }));

    [Fact]
    public void ScalarListOperation()
        => Translate(q => q
            .FilterProducts(f => f.Name(n => n.In(WidgetNames)))
            .Select(p => new { p.Name }));

    [Fact]
    public void InterfaceCompanion()
        => Translate(q => q
            .Results
            .Select(r => new
            {
                r.Id,
                Related = r.FilterRelated(f => f.Name(n => n.Eq("widget"))).Select(p => new { p.Name }),
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
