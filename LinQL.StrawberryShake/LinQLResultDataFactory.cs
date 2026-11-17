namespace LinQL.StrawberryShake;

using global::StrawberryShake;
using LinQL.Caching;

/// <summary>
/// Rebuilds a <see cref="LinQLResult{TData}"/> from the entity store.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <typeparam name="TData">The projected result type.</typeparam>
/// <param name="cache">The operation's cache hook, or <see langword="null"/> when the operation does not cache entities.</param>
/// <param name="projection">The compiled projection the rebuilt root is read through.</param>
/// <param name="store">The store whose current snapshot is read when no snapshot is given.</param>
/// <remarks>
/// The factory is created per result rather than shared, because it closes over what that result was built
/// from. Strawberry Shake holds a factory per operation and rebuilds the generated result type from ids; LinQL
/// carries the operation's root in <see cref="LinQLResultDataInfo{TRoot}"/> instead, replaces its selected
/// entities with the stored instances and projects the result again.
/// </remarks>
internal sealed class LinQLResultDataFactory<TRoot, TData>(
    LinQLOperationCache<TRoot>? cache,
    Func<TRoot, TData> projection,
    EntityStore store) : IOperationResultDataFactory<LinQLResult<TData>>, IOperationResultDataFactory
    where TRoot : RootType<TRoot>
{
    /// <inheritdoc />
    public Type ResultType => typeof(LinQLResult<TData>);

    /// <inheritdoc />
    public LinQLResult<TData> Create(IOperationResultDataInfo dataInfo, IEntityStoreSnapshot? snapshot = null)
    {
        if (dataInfo is not LinQLResultDataInfo<TRoot> info)
        {
            throw new GraphQLClientException(
                "The result was not built by LinQL, so there is nothing to rebuild it from.");
        }

        var root = info.Root;

        if (cache is not null)
        {
            root = cache.Rebuild(root, new EntityStoreReader(snapshot ?? store.CurrentSnapshot));
        }

        return new LinQLResult<TData>(
            projection(root),
            info.Errors,
            info.Extensions,
            info.ContextData,
            info.Query,
            info,
            this);
    }

    /// <inheritdoc />
    object IOperationResultDataFactory.Create(IOperationResultDataInfo dataInfo, IEntityStoreSnapshot? snapshot)
        => this.Create(dataInfo, snapshot);
}
