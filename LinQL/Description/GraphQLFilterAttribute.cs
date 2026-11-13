namespace LinQL.Description;

using System;

/// <summary>
/// Marks an extension method that attaches a filter argument to the field it is called on.
/// </summary>
/// <remarks>
/// The extension takes the field as its receiver, so <c>q.Products.Filter(f => …)</c> filters the
/// <c>products</c> field; the translator reads the field from the expression rather than from this
/// attribute. The filter is attached as the argument named by <see cref="Argument"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GraphQLFilterAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the GraphQL name of the argument that carries the filter.
    /// </summary>
    public string Argument { get; set; } = null!;

    /// <summary>
    /// Gets or sets the GraphQL type of the argument.
    /// </summary>
    public string GQLType { get; set; } = null!;
}
