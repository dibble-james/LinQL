namespace LinQL.Description;

using System;

/// <summary>
/// Marks a generated method as the filter companion of a GraphQL field.
/// </summary>
/// <remarks>
/// The companion method takes the filter for the field and returns the field, so a projection can be
/// chained on to it. The translator attaches the filter as the field argument named by
/// <see cref="Argument"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GraphQLFilterAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the GraphQL name of the field the filter applies to.
    /// </summary>
    public string Field { get; set; } = null!;

    /// <summary>
    /// Gets or sets the GraphQL name of the argument that carries the filter.
    /// </summary>
    public string Argument { get; set; } = null!;

    /// <summary>
    /// Gets or sets the GraphQL type of the argument.
    /// </summary>
    public string GQLType { get; set; } = null!;
}
