namespace LinQL.StrawberryShake;

using global::StrawberryShake;
using LinQL.Caching;

/// <summary>
/// Reads entities from a Strawberry Shake store snapshot.
/// </summary>
/// <param name="snapshot">The snapshot to read.</param>
internal sealed class EntityStoreReader(IEntityStoreSnapshot snapshot) : IEntityReader
{
    /// <inheritdoc />
    public bool TryRead(string typeName, object key, out object? entity)
        => snapshot.TryGetEntity(new EntityId(typeName, key), out entity);
}

/// <summary>
/// Writes entities through a Strawberry Shake update session, recording the ids it wrote.
/// </summary>
/// <remarks>
/// The session only exists inside the callback that <see cref="EntityStore.Update"/> hands it, so it is bound
/// before the generated write runs and its ids are read afterwards.
/// </remarks>
internal sealed class EntityStoreWriter : IEntityWriter
{
    private IEntityStoreUpdateSession? session;

    /// <summary>
    /// Gets the ids written so far, which the result's data info carries.
    /// </summary>
    public List<EntityId> Ids { get; } = [];

    /// <summary>
    /// Binds the session the write runs inside.
    /// </summary>
    /// <param name="updateSession">The session.</param>
    public void Bind(IEntityStoreUpdateSession updateSession) => this.session = updateSession;

    /// <inheritdoc />
    public bool TryRead(string typeName, object key, out object? entity)
        => this.UpdateSession().CurrentSnapshot.TryGetEntity(new EntityId(typeName, key), out entity);

    /// <inheritdoc />
    public void Write(string typeName, object key, object entity)
    {
        var id = new EntityId(typeName, key);

        this.Ids.Add(id);
        this.UpdateSession().SetEntity(id, entity);
    }

    private IEntityStoreUpdateSession UpdateSession() => this.session
        ?? throw new InvalidOperationException("An entity write ran outside the session that stores it.");
}
