namespace LinQL.Description;

/// <summary>
/// Marks a method as a GraphQL operation
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class GraphQLOperationAttribute : Attribute
{
}
