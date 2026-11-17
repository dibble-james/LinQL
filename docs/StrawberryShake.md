# Strawberry Shake

`LinQL.StrawberryShake` executes LinQL expressions over a Strawberry Shake
`IConnection<JsonDocument>` and projects the response onto your LinQL types.

## Requirements

-   `net10.0` or later. The adapter targets `net10.0` because Strawberry Shake
    16.x ships no `netstandard2.0` build; `LinQL` itself is still
    `netstandard2.0`, so this floor applies to the adapter only.
-   `StrawberryShake.Core` `16.7.3` or later. That is the version the package is
    built and tested against, and it is the earliest one with the public
    executor, document and error types the adapter uses. No HTTP or transport
    package is required by LinQL itself; use whichever Strawberry Shake
    transport you already have.

## Getting started

Wrap the connection with a LinQL client:

```csharp
using LinQL.StrawberryShake;
using StrawberryShake;

using var client = new StrawberryShakeLinQLClient(connection);
```

Or configure it through the connection extension:

```csharp
using var client = connection.WithLinQL(options =>
{
    options.LinQL = new LinQLOptions().WithKnownScalars();
    options.Serializer = new JsonSerializerOptions().WithKnownInterfaces();
    options.Strategy = ExecutionStrategy.CacheAndNetwork;
});
```

`SendAsync` executes a query or mutation and returns a single result:

```csharp
var film = await client.SendAsync((Root q) => q.ExecuteFilm("1"), cancellationToken: token);
Console.WriteLine(film.Data.Title);
```

`Watch` observes a query or a subscription:

```csharp
IObservable<LinQLResult<Film[]>> stream =
    client.Watch((Root q) => q.Films);

using var subscription = stream.Subscribe(
    result => Console.WriteLine(result.Data.Count),
    error => Console.WriteLine(error),
    () => Console.WriteLine("completed"));
```

Subscriptions must be consumed through `Watch`; `SendAsync` rejects them.

## Declared queries

A query declared with `[LinQLQuery]` is sent through the same two methods, by overloads that take the binder
the generator emits instead of an expression:

```csharp
[LinQLQuery("StarWars.Types")]
public static readonly Expression<Func<Root, string, string?>> FilmTitleById =
    (q, id) => q.ExecuteFilm(id).Select(f => f.Title);

LinQLResult<string?> film = await client.SendAsync(QueriesFilmTitleById.FilmTitleById("1"), token);

IObservable<LinQLResult<int>> numbers = client.Watch(QueriesNumbers.Numbers());
```

Both overloads take a `CancellationToken` after the query and go through the same executor as the expression
ones, so the configured `ExecutionStrategy` and the operation cache apply unchanged. The document is a
constant, so cached operations differ only by their variables. `Watch` tracks the subscription and stops it
when the client is disposed. `SendAsync` rejects a declared subscription exactly as it rejects an expression
one — a subscription is a stream, and `Watch` is the way to consume it. See
[Writing queries](Writing-Queries.md#declared-queries) for what a declaration may contain.

## Errors

A failed operation does not throw. `result.Errors` holds the `IClientError`
values from the GraphQL `errors` array — message, code, path and locations —
plus a `ClientError` wrapping the exception when the transport itself failed:

```csharp
var result = await client.SendAsync((Root q) => q.ExecuteFilm("1"));

if (result.HasErrors)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.Code}: {error.Message}");
    }
}
```

-   `result.Data` is `default` when the response has no `data` object, so check
    `HasErrors` before using it.
-   `result.Extensions` exposes the response's `extensions` object.
-   `result.Query` is the exact document that was sent, which is what to log when
    a server rejects a translated query.
-   In a `Watch` stream, GraphQL and transport errors arrive inside the result on
    `OnNext`; `OnError` is only used when the result stream itself fails.

Calling `SendAsync` with a subscription expression throws `NotSupportedException`;
use `Watch` for subscriptions. A disposed client throws
`ObjectDisposedException` on further use.

## Execution strategies and caching

The client owns its own operation cache and never shares it with a generated
Strawberry Shake client (that client's stores are private). A declared query may
also take part in the entity store, which is described under [Cache
boundary](#cache-boundary); a query written inline stays operation-level. The
operation cache:

- A finished result is stored per `OperationRequest`, which means the translated
  document plus the variable values.
- `Watch` uses `LinQLStrawberryShakeOptions.Strategy`:

| Strategy | Behaviour |
| --- | --- |
| `NetworkOnly` (default) | Always contacts the server; never replays a cached result. |
| `CacheAndNetwork` | Emits a cached result immediately, then refreshes it from the server. |
| `CacheFirst` | Emits the cached result and does not contact the server; on a cache miss it fetches from the server. |

A `CacheAndNetwork` refresh whose projected data and errors are unchanged is
not emitted a second time.

- `SendAsync` always contacts the server. Its result is written to the cache so
  a later `Watch` can replay it.
- Subscriptions always contact the server and ignore the strategy.
- Dispose the client when you are finished with it; active watchers are
  stopped and the operation and entity stores are disposed with it. Store-backed
  watchers receive `OnCompleted`; a network-only watch simply stops emitting.
  Disposing twice is safe.
- You can stop one watch without disposing the client by disposing its
  subscription or by passing a `CancellationToken` to `Watch`.

```csharp
using var client = connection.WithLinQL(
    options => options.Strategy = ExecutionStrategy.CacheFirst);
```

Because the key is the translated document plus variables, a request whose
translation is textually different (for example because it adds a field with
`Includes`) does not hit the cached entry even when it targets the same fields.

## Cache boundary

The client keeps two layers, and which one an operation takes part in follows
from how it was written.

**Declared queries are normalised.** When the schema annotates its entity types
with `@key`, the generator emits a cache hook for each `[LinQLQuery]` declaration
and attaches it to the query. The annotation is written in the schema's
`.extensions.graphql` companion, the same way `@customScalar` is:

```graphql
# A global pattern: every type that declares an `id` field is an entity.
extend schema @key(fields: "id")

# Or one type at a time, which overrides the global pattern for that type.
extend type Film @key(fields: "id")
```

A response is then written to the client's entity store alongside the
operation cache, and Strawberry Shake's operation store rebuilds the result
from entities when one of them changes:

```csharp
// Both watch the same entity, so the mutation refreshes the query's watch.
var watching = client.Watch(QueriesFilm.FilmById("1"));
await client.SendAsync(MutationsSetTitle.SetTitleById("1", "Renamed"));
```

What that means in practice:

-   A mutation or subscription that writes an entity refreshes every cached
        operation containing it, which is the behaviour a generated Strawberry
        Shake client has.
-   Two operations that select the same entity share one stored copy.
-   A rebuild replaces data and keeps the rest of the result: the errors, the
        document and the extensions the result was built with survive, so a
        field error that a later mutation fixes still shows until the operation
        is sent again.
-   A rebuild substitutes the entities the operation selected and replays its
        projection, so `result.Data` is a new object for a generated class even
        when the stored entity did not change.

**Runtime expressions stay operation-level.** An inline expression is cached by
document and variables, replayed as-is, and never rebuilt from the entity store.
A diagnostic would have to tell you that an expression opted out, and none does:
write the query as a declaration to get entity caching.

**What is not cached.** An operation takes no part in entity caching when its
selection cannot be described to the store, and it keeps the operation cache:

-   a type the schema has no `@key` pattern for is never an entity, so a schema
    without annotations caches nothing at the entity level;
-   an inline fragment selects a concrete type through an interface or union, and
    those operations are not normalised;
-   a declaration that selects an entity without selecting its key field cannot
    identify what it read, so it opts out rather than inventing an entity.

Both of the last two are reported on the declaration, because the feature's
absence is otherwise invisible: `LINQLGEN19` when the selection covers a type
that cannot be normalised, and `LINQLGEN20` when an entity was read without its
key. A schema with no `@key` patterns reports neither — entity caching is not
configured there, rather than declined.

The write merges the way a generated client's does. A selection covers part of
a type, so the properties it did not select are copied forward from the stored
entity. On a **first** write there is nothing to copy, and the unselected
properties are stored as defaults — an entity first seen through a mutation that
selects two of its fields holds defaults for the rest, and a cached operation
selecting those fields is rebuilt showing them. This is the one place where the
parity is visibly lossy.

The underlying connection is still responsible for transport-level caching (for
example HTTP caching or persisted queries).
