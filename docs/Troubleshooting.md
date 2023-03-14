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