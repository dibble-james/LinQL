namespace LinQL.StrawberryShake;

using global::StrawberryShake;

/// <summary>
/// What a <see cref="LinQLResult{TData}"/> needs to be rebuilt from an entity store.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <param name="root">The response root the result was projected from.</param>
/// <param name="entityIds">The entities the operation selected.</param>
/// <param name="version">The entity store version the result was built at.</param>
/// <param name="errors">The result's errors.</param>
/// <param name="extensions">The result's server extensions.</param>
/// <param name="contextData">The result's transport context data.</param>
/// <param name="query">The document that was sent.</param>
/// <remarks>
/// Strawberry Shake rebuilds a generated operation from the ids its result carries. LinQL cannot do that,
/// because <c>TData</c> is whatever the projection returns rather than a generated result type, so the root is
/// carried instead and the operation's generated cache replays the projection over a rebuilt copy of it.
/// Errors, extensions and the document are carried because a rebuild replaces data only: the result keeps the
/// errors it was built with, as a generated client's does.
/// </remarks>
internal sealed class LinQLResultDataInfo<TRoot>(
    TRoot root,
    IReadOnlyCollection<EntityId> entityIds,
    ulong version,
    IReadOnlyList<IClientError> errors,
    IReadOnlyDictionary<string, object?> extensions,
    IReadOnlyDictionary<string, object?> contextData,
    string query) : IOperationResultDataInfo
{
    /// <summary>
    /// Gets the root the result was projected from.
    /// </summary>
    public TRoot Root { get; } = root;

    /// <summary>
    /// Gets the errors the result was built with.
    /// </summary>
    public IReadOnlyList<IClientError> Errors { get; } = errors;

    /// <summary>
    /// Gets the server extensions the result was built with.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions;

    /// <summary>
    /// Gets the transport context data the result was built with.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ContextData { get; } = contextData;

    /// <summary>
    /// Gets the document the result was built from.
    /// </summary>
    public string Query { get; } = query;

    /// <inheritdoc />
    public IReadOnlyCollection<EntityId> EntityIds { get; } = entityIds;

    /// <inheritdoc />
    public ulong Version { get; } = version;

    /// <inheritdoc />
    public IOperationResultDataInfo WithVersion(ulong version)
        => new LinQLResultDataInfo<TRoot>(
            this.Root,
            this.EntityIds,
            version,
            this.Errors,
            this.Extensions,
            this.ContextData,
            this.Query);
}
