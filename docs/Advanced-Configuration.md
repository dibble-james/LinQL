### `<AdditionalFiles LinQLExtraNamespaces="" />`
If you have any custom scalars or types defined outside of the client that require an import, you can
add these namespaces as a `;` delimited list on the file include in the csproj.  Eg `LinQLExtraNamespaces="NodaTime;System.Spatial"`