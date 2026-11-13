namespace LinQL.Description;

using System;

/// <summary>
/// Marks a parameter to use a particular GQL type, or an extension method that attaches an argument
/// to the field it is called on.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
public class GraphQLArgumentAttribute : Attribute
{
    /// <summary>
    /// Gets the name of this type as defined by the schema.
    /// </summary>
    public string GQLType { get; set; } = null!;

    /// <summary>
    /// Gets or sets the GraphQL name of the argument when this attribute marks an extension method.
    /// </summary>
    public string? Name { get; set; }
}
