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
var film = await client.SendAsync((Root q) => q.GetFilm("1"), cancellationToken: token);
Console.WriteLine(film.Data.Title);
```

`Watch` observes a query or a subscription:

```csharp
IObservable<LinQLResult<IReadOnlyList<Film>>> stream =
    client.Watch((Root q) => q.GetFilms());

using var subscription = stream.Subscribe(
    result => Console.WriteLine(result.Data.Count),
    error => Console.WriteLine(error),
    () => Console.WriteLine("completed"));
```

Subscriptions must be consumed through `Watch`; `SendAsync` rejects them.

## Errors

A failed operation does not throw. `result.Errors` holds the `IClientError`
values from the GraphQL `errors` array — message, code, path and locations —
plus a `ClientError` wrapping the exception when the transport itself failed:

```csharp
var result = await client.SendAsync((Root q) => q.GetFilm("1"));

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
Strawberry Shake client (that client's stores are private). The cache is
operation-level only:

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
translation is textually different (for example because the includes differ)
does not hit the cached entry even when it targets the same fields.

## Cache boundary

This is a Level 1 cache. Results are stored as finished
`IOperationResult` values with no `IOperationResultDataInfo`, so:

- There is no entity normalisation: two operations that fetch the same entity
  keep two independent copies.
- Cached results are replayed as-is and are never rebuilt from an entity store,
  and entity observations do not refresh them.
- The underlying connection is still responsible for transport-level caching
  (for example HTTP caching or persisted queries).

Entity-level caching is tracked separately and is out of scope for this cache.
