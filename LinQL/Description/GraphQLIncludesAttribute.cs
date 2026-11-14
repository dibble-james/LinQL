namespace LinQL.Description;

using System;

/// <summary>
/// Marks an extension method that adds fields to the selection it is called on.
/// </summary>
/// <remarks>
/// The extension returns its receiver and has no runtime effect. The translator resolves the field the
/// method is called on and adds the fields the include lambda selects to it, so
/// <c>q.Films.Includes(f => f.Characters)</c> grows the <c>films</c> selection rather than the document
/// root.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GraphQLIncludesAttribute : Attribute
{
}
