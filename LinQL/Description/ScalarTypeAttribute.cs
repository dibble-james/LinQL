namespace LinQL.Description;

using System;

/// <summary>
/// Defines the graphql type mapped
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false)]
public class ScalarTypeAttribute : Attribute
{
    /// <summary>
    /// Creates a new instance of the <see cref="ScalarTypeAttribute"/> class.
    /// </summary>
    /// <param name="scalar">The scalar name</param>
    public ScalarTypeAttribute(string scalar) => this.Scalar = scalar;

    /// <summary>
    /// Gets the scalar name.
    /// </summary>
    public string Scalar { get; }
}
