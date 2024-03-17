namespace LinQL.Expressions;

/// <summary>
/// An expression to pick fields from union or implementation types
/// </summary>
/// <remarks>
/// Create a new <see cref="SpreadExpression"/>.
/// </remarks>
/// <param name="fieldType">The return type of the field.</param>
/// <param name="root">The root expression.</param>
public class SpreadExpression(Type fieldType, IRootExpression root) : TypeFieldExpression(GetName(fieldType), fieldType, fieldType, root)
{
    private static string GetName(Type type) => $"...on {type.GetTypeName()}";
}
