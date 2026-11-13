namespace LinQL.Description;

using System;

/// <summary>
/// Maps a filter builder method to a field of the filter input type it builds.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GraphQLFilterFieldAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the GraphQL name of the filter field.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets or sets a value indicating whether the field holds a list of sub-filters, such as
    /// <c>and</c> and <c>or</c>.
    /// </summary>
    public bool IsList { get; set; }
}
