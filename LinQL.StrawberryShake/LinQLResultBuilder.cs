namespace LinQL.StrawberryShake;

using System.Text.Json;
using global::StrawberryShake;
using global::StrawberryShake.Json;

/// <summary>
/// Builds a <see cref="LinQLResult{TData}"/> from a Strawberry Shake response document.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <typeparam name="TData">The projected result type.</typeparam>
internal sealed class LinQLResultBuilder<TRoot, TData> : IOperationResultBuilder<JsonDocument, LinQLResult<TData>>
    where TRoot : RootType<TRoot>
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    private readonly Func<TRoot, TData> projection;
    private readonly JsonSerializerOptions serializer;
    private readonly string query;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinQLResultBuilder{TRoot, TData}"/> class.
    /// </summary>
    /// <param name="projection">The compiled projection applied to the deserialised root data.</param>
    /// <param name="serializer">The serializer options used to read the response.</param>
    /// <param name="query">The document that was sent.</param>
    public LinQLResultBuilder(Func<TRoot, TData> projection, JsonSerializerOptions serializer, string query)
    {
        this.projection = projection;
        this.serializer = serializer;
        this.query = query;
    }

    /// <inheritdoc />
    public IOperationResult<LinQLResult<TData>> Build(Response<JsonDocument> response)
    {
        var errors = new List<IClientError>();
        IReadOnlyDictionary<string, object?> extensions = response.Extensions ?? Empty;
        var contextData = response.ContextData ?? Empty;
        var data = default(TData)!;

        if (response.Body is { } body && body.RootElement.ValueKind is JsonValueKind.Object)
        {
            var root = body.RootElement;

            if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind is JsonValueKind.Array)
            {
                errors.AddRange(JsonErrorParser.ParseErrors(errorsElement) ?? Array.Empty<IClientError>());
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
                }
            }
        }

        if (response.Exception is { } exception && errors.Count == 0)
        {
            errors.Add(new ClientError(exception.Message, exception: exception));
        }

        var result = new LinQLResult<TData>(data, errors, extensions, contextData, this.query);

        return new OperationResult<LinQLResult<TData>>(
            result,
            null,
            LinQLResultDataFactory<TData>.Instance,
            errors,
            extensions,
            contextData);
    }
}
