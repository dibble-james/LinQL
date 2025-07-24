namespace LinQL.Expressions;

/// <summary>
/// Access to a scalar field (a field that can have no child fields)
/// </summary>
/// <remarks>
/// Create a new <see cref="ScalarFieldExpression"/>.
/// </remarks>
/// <param name="field">The name of the field.</param>
/// <param name="fieldType">The return type of the field.</param>
/// <param name="declaringType">The .Net type that field is a member of.</param>
/// <param name="root"></param>
public class ScalarFieldExpression(string field, Type fieldType, Type declaringType, IRootExpression root)
 : FieldExpression(field, fieldType, declaringType, root)
{
}
