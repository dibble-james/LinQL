# Components

### `RootType`
This is analogous to a `DbSet` in Entity Framework. Root types are the start of all `Expressions`.

### `GraphQLExpression`
LinQL translates your `Expression` into a `GraphQLExpression` that describes what you requested into a walkable tree.  This can then be translared into the query string via `GraphQLExpressionTranslator`