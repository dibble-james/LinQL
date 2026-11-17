namespace LinQL.Caching;

/// <summary>
/// Reads normalised entities by the key the schema identifies them with.
/// </summary>
/// <remarks>
/// LinQL does not reference Strawberry Shake, and the types a generator emits are compiled into the project
/// that uses it, so the generated cache code talks to an entity store through this interface instead of the
/// adapter's <c>IEntityStoreSnapshot</c>. The Strawberry Shake adapter implements it over a store snapshot.
/// </remarks>
public interface IEntityReader
{
    /// <summary>
    /// Reads an entity.
    /// </summary>
    /// <param name="typeName">The GraphQL type name, such as <c>Film</c>.</param>
    /// <param name="key">The value of the type's <c>@key</c> field.</param>
    /// <param name="entity">The stored entity, when one is found.</param>
    /// <returns><see langword="true"/> when the store holds an entity under that key.</returns>
    /// <remarks>
    /// The entity is returned as it was written, which is a generated complex type of the same name.
    /// </remarks>
    public bool TryRead(string typeName, object key, out object? entity);
}
