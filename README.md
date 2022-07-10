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
- [x] Auto-generating client via introspection
#### Coming soon
- [ ] Subscriptions
- [ ] Custom Scalars

### Getting Started

1. Install the Nuget package

```bash
dotnet add package LinQL
```

2. Reference a `.graphql` SDL in your csproj

```xml
<ItemGroup>
    <None Remove="StarWars.graphql" />
  </ItemGroup>

  <ItemGroup>
    <AdditionalFiles Include="StarWars.graphql" LinQLClientNamespace="StarWars.Client" LinQLClientName="StarWarsGraph" />
  </ItemGroup>
```

3. Add the graph client to DI

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddStarWarsGraph().WithHttpConnection(new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index"));
}

```

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