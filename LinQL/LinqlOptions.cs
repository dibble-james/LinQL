namespace LinQL;

using LinQL.Description;

/// <summary>
/// LinQL configuration
/// </summary>
public class LinQLOptions
{
    /// <summary>
    /// Gets or sets the type name map.
    /// </summary>
    public TypeNameMap TypeNameMap { get; set; } = TypeNameMap.DefaultMappings;

    /// <summary>
    /// Gets the scalar types discovered in the schema
    /// </summary>
    public ICollection<Scalar> Scalars { get; } = new List<Scalar>();
}
