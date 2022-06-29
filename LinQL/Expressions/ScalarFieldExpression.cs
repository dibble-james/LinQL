namespace LinQL.Expressions;

/// <summary>
/// Access to a scalar field (a field that can have no child fields)
/// </summary>
public class ScalarFieldExpression : FieldExpression
{
    /// <summary>
    /// Create a new <see cref="ScalarFieldExpression"/>.
    /// </summary>
    /// <param name="field">The name of the field.</param>
    /// <param name="fieldType">The return type of the field.</param>
    /// <param name="declaringType">The .Net type that field is a member of.</param>
    public ScalarFieldExpression(string field, Type fieldType, Type declaringType)
        : base(field, fieldType, declaringType) { }
}
