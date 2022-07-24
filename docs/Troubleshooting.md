# Troubleshooting

### My root type does not inherit from `RootType<T>`
This will probably be because the SDL doesn't include a `schema` type.
Because of this, the source generator has no idea which types are the
root types.  Luckily, this is a really simple element you can pop into
the top of the SDL yourself:

```graphql
schema {
  query: RootQueryType
  mutation: RootMutationType
  subscription: RootSubscriptionType
}
```