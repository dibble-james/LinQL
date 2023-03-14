namespace LinQL.Description;

using System;

/// <summary>
/// Marks a parameter to use a particular GQL type.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class GraphQLArgumentAttribute : Attribute
{
    /// <summary>
    /// Gets the name of this type as defined by the schema.
    /// </summary>
    public string GQLType { get; set; } = null!;
}
