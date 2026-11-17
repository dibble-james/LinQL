namespace LinQL.Caching;

/// <summary>
/// The entity-cache hook of one declared operation: how its response is written to an entity store, and how it
/// is rebuilt from one.
/// </summary>
/// <typeparam name="TRoot">The root operation type.</typeparam>
/// <remarks>
/// The generator emits one subclass per declared query and the binder attaches it to the
/// <see cref="LinQLQuery{TRoot,TData}"/>, so nothing here is written by hand. Both methods are generated from the
/// operation's selection: <see cref="Write"/> knows which fields it selected, which is what lets it copy the rest
/// forward, and <see cref="Rebuild"/> reads the entities back out of the store and replays the projection over
/// them.
/// </remarks>
public abstract class LinQLOperationCache<TRoot>
    where TRoot : RootType<TRoot>
{
    /// <summary>
    /// Writes the entities an operation's response selected into the store.
    /// </summary>
    /// <param name="root">The deserialised response.</param>
    /// <param name="writer">The store to write through.</param>
    /// <returns>Nothing; the write is a side effect on the store.</returns>
    public abstract void Write(TRoot root, IEntityWriter writer);

    /// <summary>
    /// Rebuilds an operation's root from the entities the store currently holds.
    /// </summary>
    /// <param name="root">The root the operation was last built from.</param>
    /// <param name="reader">The store snapshot to read through.</param>
    /// <returns>The root to project again.</returns>
    /// <remarks>
    /// Every entity the operation selected is replaced with the stored instance, so a field another operation
    /// changed is what the projection reads. An entity that is not in the store keeps the value it already had.
    /// </remarks>
    public abstract TRoot Rebuild(TRoot root, IEntityReader reader);
}
