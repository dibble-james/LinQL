namespace LinQL.Description;

/// <summary>
/// Mark a member as a GraphQL field.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
public class GraphQLFieldAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the name the GraphQL endpoint uses for this field
    /// </summary>
    public string? Name { get; set; }
}
