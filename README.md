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
-   [x] Declared queries (`[LinQLQuery]`, translated at build time)
-   [x] Persisted operations for declared queries
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
        => await this.graph.SendAsync((Root x) => x.ExecuteFilm(id), cancellationToken);
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

var film = await client.SendAsync((Root q) => q.ExecuteFilm("1"));
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

// query linql($var1: ProductFilterInput) {
//   __typename
//   products(where: $var1) {
//     __typename
//     id
//     name
//   }
// }
// $var1 = { "price": { "gt": 10 }, "name": { "contains": "widget" } }
```

Every document carries `__typename` on each non-scalar selection and at the root, unconditionally —
the document is part of the operation cache key, so the shape is not configurable. See
[Writing queries](docs/Writing-Queries.md) for the rule, including the subscription root exception.

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

### Declared queries

A query whose shape is fixed can be written once, translated at build time, and sent without translating it
again at runtime: only its values vary, and they travel as variables. Mark a static `Expression` field with
`[LinQLQuery]`:

```csharp
using System.Linq.Expressions;
using LinQL.Description;

public static class Queries
{
    [LinQLQuery("StarWars.Types")]
    public static readonly Expression<Func<Root, string, string?>> FilmTitleById =
        (q, id) => q.ExecuteFilm(id).Select(f => f.Title);
}
```

The first lambda parameter is the schema's root type; the rest are the query's values, and the last type
argument is what the response is projected to — a projection to an anonymous type declares `object?`, since the
type cannot be named. The attribute names the `LinQLClientNamespace` of the SDL the query is written against,
because a project may generate more than one schema and the root type's name alone would be ambiguous.

The generator emits the document and a binder in the declaration's namespace: a static class named after the
declaring type and field — `QueriesFilmTitleById` for `Queries.FilmTitleById` — with a method named after the
field. Calling it reads like any other method:

```csharp
var title = await client.SendAsync(QueriesFilmTitleById.FilmTitleById("1"));

// query linql($var1: ID!) {
//   __typename
//   film(id: $var1) {
//     __typename
//     title
//   }
// }
// $var1 = "1"
```

`SendAsync` runs a declared query or mutation on both adapters. A declared subscription goes through the
adapter's stream method — `Watch` on Strawberry Shake, `CreateSubscriptionStream` on `GraphQL.Client`:

```csharp
var numbers = client.Watch(QueriesNumbers.Numbers());
var stream = graph.CreateSubscriptionStream(QueriesNumbers.Numbers());
```

Everything the document needs has to be visible to the compiler, because nothing is translated at runtime:
`Select`, `SelectAll`, `Includes`, `On` spreads, casts and `OfType` all translate, and an argument is either
one of the query's parameters — which becomes a variable, `var1`…`varN` in order — or a constant, enum member
or initialiser the compiler can see, which is written into the document. A query is bound to its values by
name, so the document never changes with them.

A declaration with any diagnostic produces **no** document: a query that silently differs from what was
written is worse than one that does not compile.

| Id | Reported when |
|----|---------------|
| `LINQLGEN08` | a field returning a type is selected without `.Select(...)` or `.SelectAll()` |
| `LINQLGEN09` | the construct cannot be translated: a member the schema does not declare, an argument the field does not declare, or a fragment the document cannot name |
| `LINQLGEN10` | a projection reads one of the query's values — the result has to depend on the response alone |
| `LINQLGEN11` | an argument's value cannot be written: it is not a parameter, a constant, an enum member, or an initialiser of those |
| `LINQLGEN12` | the named client namespace is not declared by any SDL in the project |
| `LINQLGEN13` | the schema exists but does not generate the root type the expression uses |
| `LINQLGEN14` | the member is not a static `Expression<Func<TRoot, TData>>` field |
| `LINQLGEN15` | the field is not initialised with a lambda, so there is no expression source to read |

A union or interface field needs a selection, as it does at runtime: `On((Film f) => …)`, a cast
`((Film)q.Result).Title`, or `q.Results.OfType<Film>()` each write the same `... on Film` fragment.

See [Writing Queries](docs/Writing-Queries.md#declared-queries) for the full rules, the diagnostics and the
differences from the runtime path.

### What can I do with it?
A range of expressions can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs). 
If you come accross something that doesn't work, please raise an issue or a pull request with a supporting test.

For more help check out the [docs](https://github.com/dibble-james/LinQL/wiki)

### Troubleshooting
There are a couple gotchas with the source generator that can be solved using the guides [here](https://github.com/dibble-james/LinQL/wiki/troubleshooting/)
