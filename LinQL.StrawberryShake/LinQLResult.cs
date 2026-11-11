namespace LinQL.StrawberryShake;

using global::StrawberryShake;

/// <summary>
/// The result of a LinQL operation executed through Strawberry Shake.
/// </summary>
/// <typeparam name="TData">The projected result type.</typeparam>
public sealed class LinQLResult<TData> : IOperationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinQLResult{TData}"/> class.
    /// </summary>
    /// <param name="data">The projected data.</param>
    /// <param name="errors">The GraphQL server errors.</param>
    /// <param name="extensions">Additional data provided by the server.</param>
    /// <param name="contextData">Additional data provided by the transport.</param>
    /// <param name="query">The document that was sent.</param>
    internal LinQLResult(
        TData data,
        IReadOnlyList<IClientError> errors,
        IReadOnlyDictionary<string, object?> extensions,
        IReadOnlyDictionary<string, object?> contextData,
        string query)
    {
        this.Data = data;
        this.Errors = errors;
        this.Extensions = extensions;
        this.ContextData = contextData;
        this.Query = query;
    }

    /// <summary>
    /// Gets the projected result data.
    /// </summary>
    public TData Data { get; }

    /// <summary>
    /// Gets the GraphQL document that was sent to the server.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Gets a value indicating whether the server or transport returned errors.
    /// </summary>
    public bool HasErrors => this.Errors.Count > 0;

    /// <summary>
    /// Gets the GraphQL server errors.
    /// </summary>
    public IReadOnlyList<IClientError> Errors { get; }

    /// <summary>
    /// Gets additional custom data provided by the server.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; }

    /// <summary>
    /// Gets additional custom data provided by the transport pipeline.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ContextData { get; }

    /// <inheritdoc />
    object? IOperationResult.Data => this.Data;

    /// <inheritdoc />
    Type IOperationResult.DataType => typeof(TData);

    /// <inheritdoc />
    IOperationResultDataInfo? IOperationResult.DataInfo => null;

    /// <inheritdoc />
    object IOperationResult.DataFactory => LinQLResultDataFactory<TData>.Instance;
}
