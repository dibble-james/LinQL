namespace LinQL;

using LinQL.Description;

/// <summary>
/// LinQL configuration
/// </summary>
public class LinQLOptions
{
    /// <summary>
    /// Gets the scalar types discovered in the schema
    /// </summary>
    public ICollection<Scalar> Scalars { get; } = new List<Scalar>();
}
