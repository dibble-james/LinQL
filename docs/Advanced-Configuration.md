### `<AdditionalFiles LinQLExtraNamespaces="" />`
If you have any custom scalars or types defined outside of the client that require an import, you can
add these namespaces as a `;` delimited list on the file include in the csproj.  Eg `LinQLExtraNamespaces="NodaTime;System.Spatial"`

### Custom Scalar types
LinQL will automatically setup the 5 native scalar types in GrapQL:
- `String`
- `Int`
- `Float`
- `Boolean`
- `ID`

If you're using any scalars defined outside of these, your SDL will probably include `@scalar` directives.

To be able to use them they will need a corresponding .Net type and serialiser.  The serialiser is up to you
to create or register in `JsonSerializerOptions`.  The .Net type is defined by creating a `.extensions.graphql`
file.  So if you have `schema.graphql`, you also need to add an additional file called `schema.extenions.graphql`.

```xml
<ItemGroup>
  <AdditionalFiles Include="SDLs\schema.graphql" LinQLClientNamespace="GraphQLClient" />
  <AdditionalFiles Include="SDLs\schema.extensions.graphql" DependentUpon="SDLs\schema.graphql" />
</ItemGroup>
```

Then inside the `extensions` file we need to define a GraphQL `directive` so that we can extend our scalars with
our .Net type configuration then use that directive on a particular scalar like so:

```graphql
directive @customScalar(clrType: String!) on SCALAR

extend scalar UUID
    @customScalar(clrType: "System.Guid")
```

Note that the `clrType` must be the full namespaced qualified name so that it can be matched by the query translator.