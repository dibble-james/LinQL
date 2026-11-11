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
-   [x] Strawberry Shake support

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

1. Install the Nuget package

```bash
dotnet add package LinQL
dotnet add package LinQL.StrawberryShake
```

2. Wrap your Strawberry Shake connection:

```csharp
using LinQL.StrawberryShake;
using StrawberryShake;

using var client = connection.WithLinQL(options =>
{
    options.Strategy = ExecutionStrategy.CacheAndNetwork;
});

var film = await client.SendAsync((Root q) => q.GetFilm("1"));
```

`Watch` observes queries and subscriptions; `SendAsync` executes queries and mutations.

Some things are not supported by this integration yet:

-   The cache is operation-level only: there is no entity normalisation, no cross-document dedupe, and cached results are replayed as-is rather than rebuilt from entities.
-   Each LinQL client owns its cache; it cannot share the generated Strawberry Shake client's private operation or entity stores.
-   Cache keys are the translated document plus variables, so a textually different translation of the same fields misses the cache.
-   `SendAsync` always goes to the network. `Watch` honours the configured `ExecutionStrategy`; subscriptions always go to the network.

See the [Strawberry Shake guide](https://github.com/dibble-james/LinQL/blob/main/docs/StrawberryShake.md) for usage, strategies and the cache boundary.

### What can I do with it?

A range of expressions can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs).
If you come accross something that doesn't work, please raise an issue or a pull request with a supporting test.

For more help check out the [docs](https://github.com/dibble-james/LinQL/wiki)

### Troubleshooting

There are a couple gotchas with the source generator that can be solved using the guides [here](https://github.com/dibble-james/LinQL/wiki/troubleshooting/)
