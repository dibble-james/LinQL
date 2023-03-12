namespace LinQL.Description;

using System.Collections.Generic;

/// <summary>
/// A scalar type
/// </summary>
/// <param name="Name">The graphql type name</param>
/// <param name="RuntimeType">The type represented in dotnet</param>
public record struct Scalar(string Name, string RuntimeType)
{
    internal static readonly List<Scalar> NativeScalars = new()
    {
        new Scalar("Int", "int"),
        new Scalar("Float", "float"),
        new Scalar("String", "string"),
        new Scalar("Id", "string"),
        new Scalar("Boolean", "bool"),
    };
}
