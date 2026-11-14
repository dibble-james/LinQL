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
    /// Gets or sets the GraphQL name of the argument, as the schema declares it.
    /// </summary>
    /// <remarks>
    /// The generator writes this on every parameter of an <c>Execute*</c> method, and translation names
    /// the argument in the document from it rather than from the parameter, which only matches the
    /// schema by convention. A parameter that declares no name falls back to its own.
    /// </remarks>
    public string? Name { get; set; }
}
