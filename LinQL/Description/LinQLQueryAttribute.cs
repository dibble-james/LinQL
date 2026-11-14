namespace LinQL.Description;

using System;

/// <summary>
/// Declares a query whose document is produced during code generation instead of at runtime.
/// </summary>
/// <remarks>
/// The marked member is a static field holding a parameterised expression: the first parameter is the
/// schema's root type, the rest are the query's variables, and the field's own value is never invoked.
/// The client namespace names the schema the query is written against, because a project may generate
/// several schemas and the root type's name alone does not say which one is meant.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class LinQLQueryAttribute(string clientNamespace) : Attribute
{
    /// <summary>
    /// Gets the client namespace of the schema the query is written against.
    /// </summary>
    public string ClientNamespace { get; } = clientNamespace;
}
