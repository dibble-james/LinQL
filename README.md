# LinQL

## LINQ-like access to GraphQL endpoints in .Net

[![Nuget](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml/badge.svg)](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml)

[![NuGet Badge](https://buildstats.info/nuget/LinQL)](https://www.nuget.org/packages/LinQL/)

### Supported Operations

-   [x] Queries
-   [x] Mutations
-   [x] Input Types
-   [x] Arguments
-   [x] Interfaces/Unions
-   [x] Auto-generating types via introspection
-   [x] Subscriptions
-   [x] Custom Scalars

#### Coming soon

-   [ ] Strawberry Shake support

### Getting Started using `GraphQL.Client`

1. Install the Nuget package

```bash
dotnet add package LinQL
dotnet add package LinQL.GraphQL.Client
```

2. Reference a `.graphql` SDL in your csproj

```xml
<ItemGroup>
    <AdditionalFiles Include="StarWars.graphql" LinQLClientNamespace="StarWars.Types" />
</ItemGroup>
```

3. Create a client:
```csharp
using StarWars.Types;

var client = new GraphQLHttpClient(
    "https://swapi-graphql.netlify.app/.netlify/functions/index",
    new SystemTextJsonSerializer(new JsonSerializerOptions().WithKnownInterfaces()))
    .WithLinQL(new LinQLOptions().WithKnownScalars());
```

4. Write queries!

```csharp
using GraphQL.Client.Abstractions;
using StarWars.Types; // As defined by LinQLClientNamespace

public class StarWarsClient
{
    private readonly IGraphQLClient graph;

    public StarWarsClient(IGraphQLClient graph) => this.graph = graph;

    public async Task<Film> GetFilmById(string id, CancellationToken cancellationToken)
        => await this.graph.SendAsync((Root x) => x.GetFilm(id), cancellationToken);
}
```

### Getting Started using `StrawberryShake`
Coming soon...

### What can I do with it?
A range of expressions can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs). 
If you come accross something that doesn't work, please raise an issue or a pull request with a supporting test.

For more help check out the [docs](https://github.com/dibble-james/LinQL/wiki)

### Troubleshooting
There are a couple gotchas with the source generator that can be solved using the guides [here](https://github.com/dibble-james/LinQL/wiki/troubleshooting/)
