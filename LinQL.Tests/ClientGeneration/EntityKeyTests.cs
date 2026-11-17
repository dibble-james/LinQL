namespace LinQL.Tests.ClientGeneration;

using LinQL.ClientGeneration;
using Microsoft.CodeAnalysis;
using Xunit;

public class EntityKeyTests
{
    /// <summary>
    /// A schema whose types cover every way a key can behave: a type with the global field, a type without
    /// it, an interface that declares it, an enum to key on, and a root operation type.
    /// </summary>
    private const string Sdl = """
        schema {
          query: Query
        }

        type Query {
          film: Film
          hero: Character
          product: Product
        }

        interface Node {
          id: ID!
        }

        type Film implements Node {
          id: ID!
          title: String!
          hero: Character
        }

        type Character implements Node {
          id: ID!
          name: String!
          episode: Episode
        }

        type Product {
          sku: String!
          label: String!
        }

        enum Episode {
          NEW_HOPE
          EMPIRE
        }
        """;

    [Fact]
    public void AGlobalKeyAnnotatesEveryObjectTypeThatDeclaresIt()
    {
        var model = Model("extend schema @key(fields: \"id\")");

        Assert.Equal("id", model.EntityKeys["Film"].FieldName);
        Assert.Equal("id", model.EntityKeys["Character"].FieldName);

        // A type without the field is not an entity, and neither is the root operation type or an interface.
        Assert.DoesNotContain("Product", model.EntityKeys.Keys);
        Assert.DoesNotContain("Query", model.EntityKeys.Keys);
        Assert.DoesNotContain("Node", model.EntityKeys.Keys);
    }

    [Fact]
    public void ATypeKeyOverridesTheGlobalOne()
    {
        var model = Model("""
            extend schema @key(fields: "id")
            extend type Character @key(fields: "episode")
            """);

        Assert.Equal("episode", model.EntityKeys["Character"].FieldName);
        Assert.Equal("id", model.EntityKeys["Film"].FieldName);
    }

    [Fact]
    public void ATypeKeyCanAnnotateATypeTheGlobalOneWouldMiss()
    {
        var model = Model("""
            extend schema @key(fields: "id")
            extend type Product @key(fields: "sku")
            """);

        Assert.Equal("sku", model.EntityKeys["Product"].FieldName);
        Assert.Equal("id", model.EntityKeys["Film"].FieldName);
    }

    [Fact]
    public void AnUnknownTypeKeyLeavesTheTypeUncached()
    {
        // The explicit pattern replaces the global one, and it cannot be used, so the type is not an
        // entity rather than silently falling back to the key the author replaced.
        var model = Model("""
            extend schema @key(fields: "id")
            extend type Product @key(fields: "name")
            """);

        Assert.DoesNotContain("Product", model.EntityKeys.Keys);
        Assert.Equal("id", model.EntityKeys["Film"].FieldName);
    }

    [Fact]
    public void ANonLeafTypeKeyLeavesTheTypeUncached()
    {
        var model = Model("""
            extend schema @key(fields: "id")
            extend type Film @key(fields: "hero")
            """);

        Assert.DoesNotContain("Film", model.EntityKeys.Keys);
        Assert.Equal("id", model.EntityKeys["Character"].FieldName);
    }

    [Fact]
    public void ASchemaWithoutKeysHasNoEntities()
        => Assert.Empty(Model(string.Empty).EntityKeys);

    private static SchemaModel Model(string extensions)
    {
        // The model is what these tests read, so the walk builds it without reporting: a diagnostic needs a
        // real SourceProductionContext, and the diagnostic tests drive the generator for exactly that reason.
        var context = new DocumentWalkerContext("SDLs/Entities.graphql", new SourceProductionContext(), "Entities", [], LinQLTypesAs.Class)
        {
            ReportsDiagnostics = false,
        };

        new DocumentWalker().Visit(
            HotChocolate.Language.Utf8GraphQLParser.Parse($"{Sdl}\n{extensions}"),
            context);

        return context.Model;
    }
}
