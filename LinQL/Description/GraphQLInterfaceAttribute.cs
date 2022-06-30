namespace LinQL.Description;

using System;

/// <summary>
/// Mark an interface as a GraphQL interface.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false)]
public class GraphQLInterfaceAttribute : Attribute
{
}
