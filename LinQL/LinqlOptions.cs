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
    /// <remarks>
    /// The native scalars are copied rather than shared, so generated <c>WithKnownScalars</c> calls
    /// only affect the options they are made on.
    /// </remarks>
    public ICollection<Scalar> Scalars { get; } = [.. Scalar.NativeScalars];
}
