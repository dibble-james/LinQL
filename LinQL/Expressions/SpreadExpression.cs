namespace LinQL.Expressions;

/// <summary>
/// An expression to pick fields from union or implementation types
/// </summary>
public class SpreadExpression : TypeFieldExpression
{
    /// <summary>
    /// Create a new <see cref="SpreadExpression"/>.
    /// </summary>
    /// <param name="fieldType">The return type of the field.</param>
    /// <param name="root">The root expression.</param>
    public SpreadExpression(Type fieldType, IRootExpression root)
        : base(GetName(fieldType), fieldType, fieldType, root) { }

    private static string GetName(Type type) => $"...on {type.GetTypeName()}";
}
