namespace LinQL;

/// <summary>
/// LinQL configuration
/// </summary>
public class LinqlOptions
{
    /// <summary>
    /// Gets or sets the type name map.
    /// </summary>
    public TypeNameMap TypeNameMap { get; set; } = TypeNameMap.DefaultMappings;

    /// <summary>
    /// Gets the scalar types discovered in the schema
    /// </summary>
    public ICollection<Type> Scalars { get; } = new List<Type>();
}
