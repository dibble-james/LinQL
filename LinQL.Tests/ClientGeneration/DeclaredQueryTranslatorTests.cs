namespace LinQL.Tests.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using LinQL.ClientGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

public class DeclaredQueryTranslatorTests
{
    private const string Sdl = """
        schema {
          query: Query
        }

        type Query {
          hero: Character
          heroes: [Character!]!
          character(id: ID!): Character
        }

        type Character {
          name: String!
          friend: Character
        }
        """;

    [Fact]
    public void SelectsTheScalarFieldsOfAField()
        => Snapshot.Match(Translate("q.Hero.Select(h => new { h.Name })").Document);

    [Fact]
    public void SelectAllExpandsTheScalarFieldsOfAType()
        => Snapshot.Match(Translate("q.Hero.SelectAll()").Document);

    [Fact]
    public void SelectsSeveralFieldsFromTheRoot()
        => Snapshot.Match(Translate("new { q.Hero.Select(h => new { h.Name }), q.Heroes.Select(c => c.Name) }").Document);

    [Fact]
    public void SelectsANestedField()
        => Snapshot.Match(Translate("q.Hero.Select(h => h.Friend.Select(f => new { f.Name }))").Document);

    [Fact]
    public void SelectingATypeFieldWithoutASelectionIsReported()
    {
        var (document, problems) = Translate("q.Hero");

        document.Should().BeNull();
        problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN08");

        problems[0].GetMessage().Should().Contain("'hero'").And.Contain("'Character'");
    }

    [Fact]
    public void FieldArgumentsAreReportedAsUnsupported()
    {
        var (document, problems) = Translate("""q.ExecuteCharacter("1").Select(c => new { c.Name })""");

        document.Should().BeNull();
        problems.Should().ContainSingle();

        problems[0].Id.Should().Be("LINQLGEN09");
        problems[0].GetMessage().Should().Contain("arguments are not translated yet");
    }

    private static (string? Document, IReadOnlyList<Diagnostic> Problems) Translate(string body)
    {
        var lambda = CSharpSyntaxTree.ParseText($"System.Func<object, object> query = q => {body};")
            .GetRoot()
            .DescendantNodes()
            .OfType<SimpleLambdaExpressionSyntax>()
            .First();

        var (document, problems) = DeclaredQueryTranslator.Translate(
            lambda,
            ClientGenerator.CreateModel(Sdl, "Filtering", LinQLTypesAs.Class),
            "Query");

        return (document?.ToString(), problems);
    }
}
