# Components

### `Graph`
This is analogous to a `DbContext` in Entity Framework.  It gives you access to the root operations (query, mutation, subscription) which you can then create `GraphQLExpression`s from.  It utilises a `IGraphQLConnection` to then execute the operation against the server.

It also exposes `FromRawGraphQL` methods so you can execute a raw string if your query can't be converted from an `Expression`.

### `IGraphQLConnection`
Abstracts the query transport mechanism (HTTP, WS).

### `RootType`
This is analogous to a `DbSet` in Entity Framework. Root types are the start of all `Expressions`.

### `GraphQLExpression`
LinQL translates your `Expression` into a `GraphQLExpression` that describes what you requested into a walkable tree.  This can then be translared into the query string via `GraphQLExpressionTranslator`