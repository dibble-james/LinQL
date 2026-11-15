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
          characters(ids: [ID!]!): [Character!]!
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
    public void PassesAnArgumentAsAVariableTypedByTheSchema()
        => Snapshot.Match(Translate("q.ExecuteCharacter(id: id).Select(c => new { c.Name })", "id").Document);

    [Fact]
    public void PassesAListArgumentAsAVariableOfTheDeclaredType()
        => Snapshot.Match(Translate("q.ExecuteCharacters(ids).SelectAll()", "ids").Document);

    [Fact]
    public void AParameterUsedTwiceBecomesOneVariablePerArgument()
        => Snapshot.Match(Translate("new { q.ExecuteCharacter(id).Select(c => new { c.Name }), q.ExecuteCharacter(id).SelectAll() }", "id").Document);

    [Fact]
    public void AnArgumentValueThatIsNotAParameterIsReported()
    {
        var (document, problems) = Translate("""q.ExecuteCharacter("1").Select(c => new { c.Name })""");

        document.Should().BeNull();
        problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN11");

        problems[0].GetMessage().Should().Contain("cannot become a variable");
    }

    [Fact]
    public void AnArgumentTheSchemaDoesNotDeclareIsReported()
    {
        var (document, problems) = Translate("q.ExecuteCharacter(missing: id).SelectAll()", "id");

        document.Should().BeNull();
        problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        problems[0].GetMessage().Should().Contain("'missing'");
    }

    private static Translation Translate(string body, params string[] values)
    {
        var parameters = values.Length == 0 ? "q" : $"(q, {string.Join(", ", values)})";

        var lambda = CSharpSyntaxTree.ParseText($"System.Func<object, object> query = {parameters} => {body};")
            .GetRoot()
            .DescendantNodes()
            .OfType<LambdaExpressionSyntax>()
            .First();

        var translation = DeclaredQueryTranslator.Translate(
            lambda,
            ClientGenerator.CreateModel(Sdl, "Filtering", LinQLTypesAs.Class),
            "Query");

        return new(translation.Document?.ToString(), translation.Problems);
    }

    /// <summary>
    /// What a translation produced, reduced to what these tests compare.
    /// </summary>
    /// <param name="Document">The document's text, or <see langword="null"/> when there is none.</param>
    /// <param name="Problems">What stopped the translation.</param>
    private sealed record Translation(string? Document, IReadOnlyList<Diagnostic> Problems);
}
