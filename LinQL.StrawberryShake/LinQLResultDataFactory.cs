namespace LinQL.StrawberryShake;

using global::StrawberryShake;

// The data factory for LinQLResult{TData}.
//
// LinQL results are created from a response document and the compiled projection; they are not built from an
// entity store, so there is nothing to reconstruct from an IOperationResultDataInfo.
internal sealed class LinQLResultDataFactory<TData> : IOperationResultDataFactory<LinQLResult<TData>>
{
    /// <summary>
    /// The shared factory instance.
    /// </summary>
    public static readonly LinQLResultDataFactory<TData> Instance = new();

    private const string NotSupported =
        "LinQL results are projected from the response document and cannot be rebuilt from an entity store.";

    private LinQLResultDataFactory()
    {
    }

    /// <inheritdoc />
    public Type ResultType => typeof(LinQLResult<TData>);

    /// <inheritdoc />
    public LinQLResult<TData> Create(IOperationResultDataInfo dataInfo, IEntityStoreSnapshot? snapshot = null)
        => throw new NotSupportedException(NotSupported);

    /// <inheritdoc />
    object IOperationResultDataFactory.Create(IOperationResultDataInfo dataInfo, IEntityStoreSnapshot? snapshot)
        => throw new NotSupportedException(NotSupported);
}
