# GraphQL.Client

If you're using graphql-dotnet's GraphQL.Client, you can use Linq by
using the overload extension methods on `GraphQL.Client.Abstractions.IGraphQLClient`.

To execute queries and mutations use:

```csharp
Task<GraphQLExpressionResponse<TRoot, TData>> SendAsync<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>
```

To start a subscription use:

````csharp
IObservable<GraphQLExpressionResponse<TRoot, TData>> CreateSubscriptionStream<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null)
        where TRoot : RootType<TRoot>
```csharp

Both return a `GraphQLExpressionResponse<TRoot, TData>` which is just a
`GraphQLResponse<TData>` but with the translated expression available
so you can debug when was sent to the server.
````
