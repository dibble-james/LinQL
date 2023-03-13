namespace LinQL.Description;

using System.Collections.Generic;

/// <summary>
/// A scalar type
/// </summary>
/// <param name="Name">The graphql type name</param>
/// <param name="RuntimeType">The type represented in dotnet</param>
public record struct Scalar(string Name, string RuntimeType)
{
    /// <summary>
    /// Creates a new instance of the <see cref="Scalar"/> type.
    /// </summary>
    /// <param name="name">The graphql type name</param>
    /// <param name="runtimeType">The type represented in dotnet</param>
    /// <param name="originalPrimitive">The System primitive the runtime type is mapped too.</param>
    public Scalar(string name, string runtimeType, string originalPrimitive)
        : this(name, runtimeType) => this.OriginalPrimitive = originalPrimitive;

    internal static readonly List<Scalar> NativeScalars = new()
    {
        new Scalar("Int", "int", "System.Int32"),
        new Scalar("Float", "float", "System.Single"),
        new Scalar("String", "string", "System.String"),
        new Scalar("Id", "string", "System.String"),
        new Scalar("Boolean", "bool", "System.Boolean"),
    };

    /// <summary>
    /// Gets the System primitive the runtime type is mapped too.
    /// </summary>
    public string? OriginalPrimitive { get; }
}
