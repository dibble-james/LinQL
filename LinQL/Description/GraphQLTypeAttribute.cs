namespace LinQL.Description;

/// <summary>
/// Marks a class as a GraphQL type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class GraphQLTypeAttribute : Attribute
{
    /// <summary>
    /// Gets the name of this type as defined by the schema.
    /// </summary>
    public string? Name { get; set; }
}
