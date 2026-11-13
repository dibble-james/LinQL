### `<LinQLTypesAs>Class|Record</LinQLTypesAs>`
The generated client's types are `partial class`es by default. Set `LinQLTypesAs` to `Record` to
generate `partial record`s instead, which brings value equality, `with` expressions and a generated
`ToString`:

```xml
<PropertyGroup>
  <LinQLTypesAs>Record</LinQLTypesAs>
</PropertyGroup>
```

Only the schema's object types, input objects and root types change. GraphQL interfaces stay C#
interfaces and the fluent filter builders stay classes. Any `partial` declaration you add yourself has
to be a `record` as well once the setting is on. A value other than `Class` or `Record` is reported as
`LINQLGEN07` and generation falls back to classes.

Properties become `{ get; init; }` rather than `{ get; set; }`, so a deserialised or constructed result
is immutable and a `with` expression is the way to change one. GraphQL interfaces get the same
accessor, because a record with an init-only property cannot implement an interface that declares a
setter. Two things follow: `init` needs `System.Runtime.CompilerServices.IsExternalInit`, which .NET 5+
and the usual polyfill packages (PolySharp and friends) provide; and deserialisation relies on
System.Text.Json 5.0 or later, which is the first version that populates init-only properties. Object
initialisers already work, so `new ProductSortInput { Price = SortEnumType.DESC }` still compiles.

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