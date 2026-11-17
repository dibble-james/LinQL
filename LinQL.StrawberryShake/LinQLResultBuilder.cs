namespace LinQL.StrawberryShake;

using System.Text.Json;
using global::StrawberryShake;
using global::StrawberryShake.Json;
using LinQL.Caching;

// Builds a LinQLResult{TData} from a Strawberry Shake response document.
internal sealed class LinQLResultBuilder<TRoot, TData>(
    Func<TRoot, TData> projection,
    JsonSerializerOptions serializer,
    string query,
    LinQLOperationCache<TRoot>? cache,
    EntityStore store) : IOperationResultBuilder<JsonDocument, LinQLResult<TData>>
    where TRoot : RootType<TRoot>
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    private readonly Func<TRoot, TData> projection = projection;
    private readonly JsonSerializerOptions serializer = serializer;
    private readonly string query = query;
    private readonly LinQLOperationCache<TRoot>? cache = cache;
    private readonly EntityStore store = store;

    /// <inheritdoc />
    public IOperationResult<LinQLResult<TData>> Build(Response<JsonDocument> response)
    {
        var errors = new List<IClientError>();
        var extensions = response.Extensions ?? Empty;
        var contextData = response.ContextData ?? Empty;
        var data = default(TData)!;
        var dataInfo = default(LinQLResultDataInfo<TRoot>);

        if (response.Body is { } body && body.RootElement.ValueKind is JsonValueKind.Object)
        {
            var root = body.RootElement;

            if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind is JsonValueKind.Array)
            {
                errors.AddRange(JsonErrorParser.ParseErrors(errorsElement) ?? []);
            }

            if (root.TryGetProperty("extensions", out var extensionsElement) && extensionsElement.ValueKind is JsonValueKind.Object)
            {
                extensions = extensionsElement.Deserialize<Dictionary<string, object?>>(this.serializer) ?? extensions;
            }

            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind is JsonValueKind.Object)
            {
                var rootData = dataElement.Deserialize<TRoot>(this.serializer);

                if (rootData is not null)
                {
                    data = this.projection(rootData);
                    dataInfo = this.Write(rootData, errors, extensions, contextData);
                }
            }
        }

        if (response.Exception is { } exception && errors.Count == 0)
        {
            errors.Add(new ClientError(exception.Message, exception: exception));
        }

        var factory = new LinQLResultDataFactory<TRoot, TData>(this.cache, this.projection, this.store);
        var result = new LinQLResult<TData>(data, errors, extensions, contextData, this.query, dataInfo, factory);

        return new OperationResult<LinQLResult<TData>>(
            result,
            dataInfo,
            factory,
            errors,
            extensions,
            contextData);
    }

    // Writes the response's entities to the store and describes the result for a later rebuild.
    //
    // One update session covers the whole response, so every entity a document selected is stored at the same
    // version and the operation store sees one update rather than one per entity.
    //
    // Returns the data info, or null when the operation does not cache entities.
    private LinQLResultDataInfo<TRoot>? Write(
        TRoot root,
        IReadOnlyList<IClientError> errors,
        IReadOnlyDictionary<string, object?> extensions,
        IReadOnlyDictionary<string, object?> contextData)
    {
        if (this.cache is null)
        {
            return null;
        }

        var writer = new EntityStoreWriter();

        this.store.Update(session =>
        {
            writer.Bind(session);
            this.cache.Write(root, writer);
        });

        return new LinQLResultDataInfo<TRoot>(
            root,
            writer.Ids,
            this.store.CurrentSnapshot.Version,
            errors,
            extensions,
            contextData,
            this.query);
    }
}
