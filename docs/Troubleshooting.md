# Troubleshooting

### My root type does not inherit from `RootType<T>`
This will probably be because the SDL doesn't include a `schema` type.
Because of this, the source generator has no idea which types are the
root types.  Luckily, this is a really simple element you can pop into
the top of the SDL or a `.extensions.graphql` file yourself:

```graphql
schema {
  query: RootQueryType
  mutation: RootMutationType
  subscription: RootSubscriptionType
}
```

### Client generation errors
```
LINQLGEN02: GraphQL file {0} does not have a LinQLClientNamespace attribute. Please add it to the AdditionalFiles element in your csproj file.
```
This could be related to a Nuget bug where the `LinQL.props` file has not been imported because it won't follow transitive references.
Install the `LinQL` package individually.

### A filter input type is missing from the SDL
```
LINQLGEN05: Filter argument 'where' on 'products' has type 'ProductFilterInput', but the SDL does not define that as an input object. Export the SDL from a server with filtering configured so the filter input types are included.
```
The generator found a field argument whose type looks like a filter input, but the SDL it was given
has no `input` declaration for that type, so the generated extensions and builders would not
compile.
Hot Chocolate only writes the filter input types into the exported SDL when the schema actually uses
filtering, and Hot Chocolate 16 disables introspection by default: export the SDL from the running
server with `GET {url}?sdl`, or use the Hot Chocolate CLI, and make sure the filtering middleware is
configured on the field.