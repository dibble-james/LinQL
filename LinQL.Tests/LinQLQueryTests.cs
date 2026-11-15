namespace LinQL.Tests;

using System.Collections.Generic;
using System.Linq.Expressions;
using LinQL.Description;

public class LinQLQueryTests
{
    private static readonly Expression<Func<DeclaredQueryRoot, string>> Name = q => q.Name;

    private static readonly Expression<Func<DeclaredQueryRoot, object?>> Whole = q => q;
    [Fact]
    public void BindCarriesTheDocumentItsOperationAndEveryVariable()
    {
        var query = LinQLQuery<DeclaredQueryRoot, string>.Bind(
            "query linql($var1: ID!) { __typename character(id: $var1) { __typename name } }",
            RootOperationType.Query,
            Name,
            ("var1", "1"),
            ("var2", null));

        query.Document.Should().StartWith("query linql($var1:");
        query.OperationType.Should().Be(RootOperationType.Query);
        query.Variables.Should().HaveCount(2);
        query.Variables["var1"].Should().Be("1");
        query.Variables["var2"].Should().BeNull();
        query.Projection.Should().NotBeNull();
    }

    [Fact]
    public void BindNormalisesValuesTheWayRuntimeArgumentsAre()
    {
        var query = LinQLQuery<DeclaredQueryRoot, object?>.Bind(
            "query linql($var1: SearchInput) { __typename }",
            RootOperationType.Query,
            Whole,
            ("var1", new { Name = "widget" }));

        var value = query.Variables["var1"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;

        value.Should().ContainKey("name").WhoseValue.Should().Be("widget");
    }
}

/// <summary>
/// The root type the declared queries below are written against.
/// </summary>
[OperationType(RootOperationType.Query)]
public sealed class DeclaredQueryRoot : RootType<DeclaredQueryRoot>
{
    /// <summary>
    /// Gets or sets the field the projection reads.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
