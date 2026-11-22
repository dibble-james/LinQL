# GraphQL.Client

If you're using graphql-dotnet's GraphQL.Client, you can use Linq by
using the overload extension methods on `GraphQL.Client.Abstractions.IGraphQLClient`.

But first you need to wrap it with configuration like so:
```csharp
var client = new GraphQLHttpClient(
    "https://swapi-graphql.netlify.app/.netlify/functions/index",
    new SystemTextJsonSerializer(new JsonSerializerOptions().WithKnownInterfaces()))
    .WithLinQL(new LinQLOptions().WithKnownScalars());
```

If you're using `HTTPClientFactory` you'd register it this way:
```csharp
builder.Services.AddHttpClient<IGraphQLClient, LinqlGraphQLClient>(
    http => new LinqlGraphQLClient(
        new GraphQLHttpClient(
            new GraphQLHttpClientOptions { EndPoint = new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index") },
            new SystemTextJsonSerializer().WithKnownInterfaces(),
            http),
        new LinQL.LinqlOptions().WithKnownScalars()))
```

To execute queries and mutations use:

```csharp
Task<GraphQLExpressionResponse<TRoot, TData>> SendAsync<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>
```

To start a subscription use:

````csharp
IObservable<GraphQLExpressionResponse<TRoot, TData>> CreateSubscriptionStream<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request)
        where TRoot : RootType<TRoot>
```csharp

Both return a `GraphQLExpressionResponse<TRoot, TData>` which is just a
`GraphQLResponse<TData>` but with the translated expression available
so you can debug when was sent to the server.

## Declared queries

A query declared with `[LinQLQuery]` has no expression to translate, so it is sent through its own
overloads:

```csharp
Task<LinQLQueryResponse<TData>> SendAsync<TRoot, TData>(
        this IGraphQLClient client,
        LinQLQuery<TRoot, TData> query,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>

IObservable<LinQLQueryResponse<TData>> CreateSubscriptionStream<TRoot, TData>(
        this IGraphQLClient client,
        LinQLQuery<TRoot, TData> query)
        where TRoot : RootType<TRoot>
```

The binder the generator emits supplies the document and the values:

```csharp
[LinQLQuery("StarWars.Types")]
public static readonly Expression<Func<Root, string, string?>> FilmTitleById =
    (q, id) => q.ExecuteFilm(id).Select(f => f.Title);

var title = await client.SendAsync(QueriesFilmTitleById.FilmTitleById("1"));
```

A declared subscription has to go through `CreateSubscriptionStream`, because a subscription is a stream
rather than one response — `SendAsync` throws `NotSupportedException` for it:

```csharp
using var stream = graph
    .CreateSubscriptionStream(QueriesNumbers.Numbers())
    .Subscribe(response => Console.WriteLine(response.Data.Number));
```

`LinQLQueryResponse<TData>` carries `Document` — the text that was sent, which is the generated constant
here — alongside `Data`, `Errors` and `Extensions`, so a declared query is as easy to debug as a translated
one. See [Writing queries](Writing-Queries.md#declared-queries) for what a declaration may contain.

The generated registry described under [persisted operations](Writing-Queries.md#persisted-operations) can be
registered on the server for either adapter, but this one always sends the document: graphql-dotnet's
`GraphQLRequest` has no operation-id field, so `IGraphQLClient` has no way to carry one. The Strawberry Shake
adapter sends the id when `PersistedOperations` is enabled.
````
