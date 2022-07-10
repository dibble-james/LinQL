# Advanced Configuration

The included source generator creates a quick-start service collection configuration. If you
want more control over how your client is registered you can manually configure then client.

```csharp
services.AddGraphQLClient<StarWarsGraph>()
```

This exposes a `GraphBuilder<T>` which includes a couple methods.

### `WithHttpConnection(Action<HttpClient>)`

This gives you complete control of the underlying `HttpClient` so you can add any custom headers, `DelegatingHandlers` for authentication etc.

### `ConfigureSerialization(Action<JsonSerializerOptions>)`

Here you can register any extra serializers, convetions, etc that are required to enable you to serialise and
deserialise requests and responses from the server.

### `<AdditionalFiles LinQLExtraNamespaces="" />`
If you have any custom scalars or types defined outside of the client that require an import, you can
add these namespaces as a `;` delimited list on the file include in the csproj.  Eg `LinQLExtraNamespaces="NodaTime;System.Spatial"`