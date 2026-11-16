namespace LinQL.Caching;

/// <summary>
/// Writes normalised entities, and reads them so a partial write can copy the fields it did not select.
/// </summary>
/// <remarks>
/// A generated write reads the current entity before storing the merge, because a selection covers part of a
/// type: the properties it did not select are copied forward from what the store already holds, and filled with
/// <c>default!</c> when the entity is stored for the first time. That is the merge a generated Strawberry
/// Shake client performs, and it is why this interface extends <see cref="IEntityReader"/>.
/// </remarks>
public interface IEntityWriter : IEntityReader
{
    /// <summary>
    /// Stores an entity, replacing whatever the store held under the same key.
    /// </summary>
    /// <param name="typeName">The GraphQL type name, such as <c>Film</c>.</param>
    /// <param name="key">The value of the type's <c>@key</c> field.</param>
    /// <param name="entity">The entity to store, already merged with what the store held.</param>
    public void Write(string typeName, object key, object entity);
}
