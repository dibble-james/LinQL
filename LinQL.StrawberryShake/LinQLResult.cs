namespace LinQL.StrawberryShake;

using global::StrawberryShake;

/// <summary>
/// The result of a LinQL operation executed through Strawberry Shake.
/// </summary>
/// <typeparam name="TData">The projected result type.</typeparam>
public sealed class LinQLResult<TData> : IOperationResult, IEquatable<LinQLResult<TData>>
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

    /// <summary>
    /// Determines whether the specified result has the same projected data and errors as this result.
    /// </summary>
    /// <param name="other">The result to compare.</param>
    /// <returns><c>true</c> when the data and errors are equal; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// Errors are compared by message, code and path. The query text, server extensions, context data and error
    /// locations are not part of the comparison.
    /// </remarks>
    public bool Equals(LinQLResult<TData>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return EqualityComparer<TData>.Default.Equals(this.Data, other.Data)
            && this.Errors.Count == other.Errors.Count
            && this.Errors.Zip(other.Errors, ErrorEquals).All(equal => equal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LinQLResult<TData> other && this.Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(this.Data);

        foreach (var error in this.Errors)
        {
            hash.Add(error.Message);
            hash.Add(error.Code);

            foreach (var segment in error.Path ?? Array.Empty<object>())
            {
                hash.Add(segment);
            }
        }

        return hash.ToHashCode();
    }

    private static bool ErrorEquals(IClientError first, IClientError second)
        => string.Equals(first.Message, second.Message, StringComparison.Ordinal)
            && string.Equals(first.Code, second.Code, StringComparison.Ordinal)
            && (first.Path ?? Array.Empty<object>()).SequenceEqual(second.Path ?? Array.Empty<object>());
}
