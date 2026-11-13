# LinQL

## LINQ-like access to GraphQL endpoints in .Net

[![Nuget](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml/badge.svg)](https://github.com/dibble-james/LinQL/actions/workflows/nuget.yml)

[![NuGet Badge](https://buildstats.info/nuget/LinQL)](https://www.nuget.org/packages/LinQL/)

### Supported Operations

-   [x] Queries
-   [x] Mutations
-   [x] Input Types
-   [x] Arguments
-   [x] Hot Chocolate filtering (`where` arguments)
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

### Filtering

When the endpoint exposes Hot Chocolate filter arguments (`products(where: ProductFilterInput)`),
the generator adds a `Filter` extension method for the field, and LinQL sends your filter as the
`where` variable:

```csharp
var products = await client.SendAsync((Root q) => q
    .Products
    .Filter(f => f
        .Price(p => p.Gt(10))
        .Name(n => n.Contains("widget")))
    .Select(p => new { p.Id, p.Name }));

// query linql($var1: ProductFilterInput) { products(where: $var1) { id name } }
// $var1 = { "price": { "gt": 10 }, "name": { "contains": "widget" } }
```

The operations are the schema's own (`gt`, `contains`, `some`, `all`, `none`, `and`, `or`, …),
so anything the endpoint advertises works and anything it doesn't is rejected. Only the operations
you set are sent: unset properties are dropped, and a filter with nothing set throws rather than
sending `{}`, which Hot Chocolate treats as an error on some versions and as no filter on others.

Other arguments of the field get extensions too, named after them, so a paged or sorted field stays
readable: `q.Featured.First(10).Filter(f => f.Price(p => p.Gt(10))).Select(p => new { p.Name })`.

`.Where` is **not** mapped onto this. It is applied client-side after the response today, and
mapping it would silently change results: its C# semantics are not Hot Chocolate's, and it would
move work to the server without the caller asking. Filtering stays explicit and one-to-one with
the schema.

See [Writing Queries](https://github.com/dibble-james/LinQL/blob/main/docs/Writing-Queries.md#filtering)
for nested, `and`/`or` and collection filters, and the null rules.

### What can I do with it?
A range of expressions can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs). 
If you come accross something that doesn't work, please raise an issue or a pull request with a supporting test.

For more help check out the [docs](https://github.com/dibble-james/LinQL/wiki)

### Troubleshooting
There are a couple gotchas with the source generator that can be solved using the guides [here](https://github.com/dibble-james/LinQL/wiki/troubleshooting/)
