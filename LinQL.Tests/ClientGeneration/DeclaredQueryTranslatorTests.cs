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
          search(where: SearchInput): [Character!]!
          episodes(only: Episode): [Character!]!
          result: Result
          results: [Result!]!
          film: Film
        }

        type Film {
          title: String!
          hero: Character
        }

        union Result = Character | Film

        type Character {
          name: String!
          friend: Character
        }

        input SearchInput {
          name: String
          minAge: Int
          friends: [SearchInput!]
        }

        enum Episode {
          NEW_HOPE
          EMPIRE
        }
        """;

    /// <summary>
    /// A schema with all three roots, so the operation a declaration writes can be the mutation or the
    /// subscription one.
    /// </summary>
    private const string MultiRootSdl = """
        schema {
          query: Query
          mutation: Mutation
          subscription: Subscription
        }

        type Query {
          hero: Character
        }

        type Mutation {
          rename(name: String!): Character
        }

        type Subscription {
          renamed: Character
        }

        type Character {
          name: String!
          friend: Character
        }
        """;

    /// <summary>
    /// The framework a value has to resolve against for the compiler to see it.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => MetadataReference.CreateFromFile(x)),
    ];

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
    public void IncludesAddsFieldsToASelection()
        => Snapshot.Match(Translate("q.Hero.Includes(h => h.Name)").Document);

    [Fact]
    public void IncludesGrowsTheSelectionASelectAlreadyMade()
        => Snapshot.Match(Translate("q.Hero.Select(h => new { h.Name }).Includes(h => h.Friend.Select(f => new { f.Name }))").Document);

    [Fact]
    public void IncludesOnTheLambdasOwnParameterGrowsThatSelection()
        => Snapshot.Match(Translate("q.Hero.Select(h => h.Includes(x => x.Name))").Document);

    [Fact]
    public void IncludesOnACollectionAddsFieldsToTheElementsSelection()
        => Snapshot.Match(Translate("q.Heroes.Includes(c => c.Name)").Document);

    [Fact]
    public void AMutationWritesItsOperationAndKeepsTheRootTypeName()
        => Snapshot.Match(Translate("q.ExecuteRename(\"a\").Select(c => new { c.Name })", sdl: MultiRootSdl, rootType: "Mutation").Document);

    [Fact]
    public void ASubscriptionOmitsTheRootTypeName()
        => Snapshot.Match(Translate("q.Renamed.Select(c => new { c.Name })", sdl: MultiRootSdl, rootType: "Subscription").Document);

    [Fact]
    public void WritesAnInlineFragmentForASpread()
        => Snapshot.Match(Translate("q.Result.On((Character c) => c.Name)").Document);

    [Fact]
    public void ChainedSpreadsWriteSiblingFragments()
        => Snapshot.Match(Translate("q.Result.On((Character c) => c.Name).On((Film f) => f.Title)").Document);

    [Fact]
    public void SpreadOnTheLambdasOwnParameterGrowsThatSelection()
        => Snapshot.Match(Translate("q.Hero.Select(h => h.On((Character c) => c.Name))").Document);

    [Fact]
    public void WritesAFragmentForACastOnAField()
        => Snapshot.Match(Translate("((Character)q.Result).Name").Document);

    [Fact]
    public void WritesAFragmentForAnAsExpression()
        => Snapshot.Match(Translate("(q.Result as Character).Name").Document);

    [Fact]
    public void WritesAFragmentForACastOnAParameter()
        => Snapshot.Match(Translate("q.Results.Select(r => ((Character)r).Name)").Document);

    [Fact]
    public void ABareCastSelectsTheTypeScalarFields()
        => Snapshot.Match(Translate("(Character)q.Result").Document);

    [Fact]
    public void WritesAFragmentForOfType()
        => Snapshot.Match(Translate("q.Results.OfType<Character>()").Document);

    [Fact]
    public void WritesAFragmentForOfTypeWithASelection()
        => Snapshot.Match(Translate("q.Results.OfType<Film>().Select(f => new { f.Title })").Document);

    [Fact]
    public void ACastToATypeTheSchemaDoesNotDeclareIsReported()
    {
        var translation = Translate("((Nope)q.Result).Name");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("'Nope'");
    }

    [Fact]
    public void AnOfTypeOfATypeTheSchemaDoesNotDeclareIsReported()
    {
        var translation = Translate("q.Results.OfType<Nope>()");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("'Nope'");
    }

    [Fact]
    public void ASpreadWithoutATypeNameIsReported()
    {
        var translation = Translate("q.Result.On(c => c.Name)");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("has to name the type");
    }

    [Fact]
    public void ASpreadOfATypeTheSchemaDoesNotDeclareIsReported()
    {
        var translation = Translate("q.Result.On((Nope n) => n.Whatever)");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("'Nope'");
    }

    [Fact]
    public void AValuePassedToIncludesIsReported()
    {
        var translation = Translate("q.Hero.Includes(name)", ["name"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN10");

        translation.Problems[0].GetMessage().Should().Contain("'name'");
    }

    [Fact]
    public void AnIncludeThatIsNotALambdaIsReported()
    {
        var translation = Translate("q.Hero.Includes(q)");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("an include has to be an expression selecting fields");
    }

    [Fact]
    public void SelectingATypeFieldWithoutASelectionIsReported()
    {
        var translation = Translate("q.Hero");

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN08");

        translation.Problems[0].GetMessage().Should().Contain("'hero'").And.Contain("'Character'");
    }

    [Fact]
    public void PassesAnArgumentAsAVariableTypedByTheSchema()
        => Snapshot.Match(Translate("q.ExecuteCharacter(id: id).Select(c => new { c.Name })", ["id"]).Document);

    [Fact]
    public void PassesAListArgumentAsAVariableOfTheDeclaredType()
        => Snapshot.Match(Translate("q.ExecuteCharacters(ids).SelectAll()", ["ids"]).Document);

    [Fact]
    public void AParameterUsedTwiceBecomesOneVariablePerArgument()
        => Snapshot.Match(Translate("new { q.ExecuteCharacter(id).Select(c => new { c.Name }), q.ExecuteCharacter(id).SelectAll() }", ["id"]).Document);

    [Fact]
    public void WritesALiteralValue()
        => Snapshot.Match(Translate("""q.ExecuteCharacter("1").SelectAll()""").Document);

    [Fact]
    public void WritesAConstantValue()
        => Snapshot.Match(Translate("q.ExecuteCharacter(Id).SelectAll()", declarations: """public const string Id = "1";""").Document);

    [Fact]
    public void WritesAnEnumMemberValue()
        => Snapshot.Match(Translate("q.ExecuteEpisodes(Episode.EMPIRE).SelectAll()", declarations: "public enum Episode { NEW_HOPE, EMPIRE }").Document);

    [Fact]
    public void WritesAListInitialiser()
        => Snapshot.Match(Translate("""q.ExecuteCharacters(new[] { "1", "2" }).SelectAll()""").Document);

    [Fact]
    public void WritesAnObjectInitialiser()
        => Snapshot.Match(Translate("""q.ExecuteSearch(new SearchInput { Name = "hero", Friends = new[] { new SearchInput { Name = "other" } } }).SelectAll()""").Document);

    [Fact]
    public void WritesAParameterInsideAnInitialiserAsAVariable()
        => Snapshot.Match(Translate("q.ExecuteSearch(new SearchInput { Name = name }).SelectAll()", ["name"]).Document);

    [Fact]
    public void AValueTheCompilerCannotSeeIsReported()
    {
        var translation = Translate("q.ExecuteCharacter(id.ToUpper()).SelectAll()", ["id"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN11");

        translation.Problems[0].GetMessage().Should().Contain("cannot become a value");
    }

    [Fact]
    public void AProjectionThatReadsAValueIsReported()
    {
        var translation = Translate("q.Hero.Select(h => new { h.Name, Prefix = name })", ["name"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN10");

        translation.Problems[0].GetMessage().Should().Contain("'name'");
    }

    [Fact]
    public void AProjectionThatReadsAPartOfAValueIsReported()
    {
        var translation = Translate("q.Hero.Select(h => name.ToUpper())", ["name"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN10");

        translation.Problems[0].GetMessage().Should().Contain("'name'");
    }

    [Fact]
    public void AValuePassedAsTheProjectionIsReported()
    {
        var translation = Translate("q.Hero.Select(name)", ["name"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN10");

        translation.Problems[0].GetMessage().Should().Contain("'name'");
    }

    [Fact]
    public void AnArgumentTheSchemaDoesNotDeclareIsReported()
    {
        var translation = Translate("q.ExecuteCharacter(missing: id).SelectAll()", ["id"]);

        translation.Document.Should().BeNull();
        translation.Problems.Should().ContainSingle()
            .Which.Id.Should().Be("LINQLGEN09");

        translation.Problems[0].GetMessage().Should().Contain("'missing'");
    }

    /// <summary>
    /// Translates a body written in a class, so a const or an enum can be declared beside it.
    /// </summary>
    /// <param name="body">The lambda's body.</param>
    /// <param name="values">The declaration's lambda parameters after the root.</param>
    /// <param name="declarations">Members the body may refer to.</param>
    /// <returns>What the translation produced.</returns>
    private static Translation Translate(
        string body,
        string[]? values = null,
        string declarations = "",
        string sdl = Sdl,
        string rootType = "Query")
    {
        var parameters = values is null || values.Length == 0 ? "q" : $"(q, {string.Join(", ", values)})";

        var source = $$"""
            namespace Values
            {
                public static class Holder
                {
                    {{declarations}}

                    public static readonly System.Func<object, object> Query = {{parameters}} => {{body}};
                }
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);

        var lambda = tree.GetRoot().DescendantNodes().OfType<LambdaExpressionSyntax>().First();

        var semantics = CSharpCompilation.Create("values", [tree], References).GetSemanticModel(tree);

        var translation = DeclaredQueryTranslator.Translate(
            lambda,
            ClientGenerator.CreateModel(sdl, "Filtering", LinQLTypesAs.Class),
            rootType,
            DeclaredValues.ReadAll(lambda, semantics));

        return new(translation.Document?.ToString(), translation.Problems);
    }

    /// <summary>
    /// What a translation produced, reduced to what these tests compare.
    /// </summary>
    /// <param name="Document">The document's text, or <see langword="null"/> when there is none.</param>
    /// <param name="Problems">What stopped the translation.</param>
    private sealed record Translation(string? Document, IReadOnlyList<Diagnostic> Problems);
}
