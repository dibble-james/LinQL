# LinQL

## LINQ-like access to GraphQL endpoints in .Net

[![Nuget](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml/badge.svg)](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml)

[![NuGet Badge](https://buildstats.info/nuget/LinQL)](https://www.nuget.org/packages/LinQL/)

### Supported Operations
- [x] Queries
- [x] Mutations
- [x] Input Types
- [x] Arguments
- [x] Interfaces/Unions
#### Coming soon
- [ ] Subscriptions
- [ ] Auto-generating client via introspection

### Getting Started

1. Install the Nuget package

```bash
dotnet add package LinQL
```

2. Create a `Graph` and a `RootType`

```csharp
public class StarWarsGraph : Graph
{
    public StarWarsGraph(IGraphQLConnection connection, IQueryTranslator queryTranslator)
        : base(connection, queryTranslator)
    {
    }

    public StarWarsQueries Query => this.RootType<StarWarsQueries>();
}

[OperationType]
public class StarWarsQueries : RootType<StarWarsQueries>
{
    public Film Film { get; set; }

    [GraphQLOperation, GraphQLField(Name = "film")]
    public Film GetFilm(string id) => this.Film;
}

public class Film
{
    [GraphQLField(Name = "ID")]
    public string? Id { get; set; }

    [GraphQLField(Name = "episodeID")]
    public int EpisodeId { get; set; }

    public string? Title { get; set; }
}
```

3. Add a graph client to DI

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddGraphQLClient<StarWarsGraph>()
            .WithHttpConnection(c => c.BaseAddress = new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index"))
}

```

Underneath, `WithHttpConnection` is just setting up `IHttpClientFactory` so anything you can configure with that, you can configure here; authentication headers, middleware etc.

4. Write queries!

```csharp
public class StarWarsClient
{
    private readonly StarWarsGraph graph;

    public StarWarsClient(StarWarsGraph graph) => this.graph = graph;

    public async Task<Film> GetFilmById(string id, CancellationToken cancellationToken)
        => await this.graph.Query.Select(x => x.GetFilm(id)).Execute(cancellationToken);
}
```

For more help check out the [docs](./docs/)

A range of expressions can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs).  If you come accross something that doesn't work, please raise an issue or a pull request with a supporting test.