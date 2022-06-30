namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// A query of a field.
/// </summary>
public abstract class FieldExpression : Expression
{
    /// <param name="field">The name of the field.</param>
    /// <param name="fieldType">The .Net return type of the field.</param>
    /// <param name="declaringType">The .Net type that field is a member of.</param>
    protected FieldExpression(string field, Type fieldType, Type declaringType)
        => (this.FieldName, this.Type, this.DeclaringType) = (field, fieldType, declaringType);

    /// <summary>
    /// Gets the GraphQL field name.
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Gets the expression type.
    /// </summary>
    public override ExpressionType NodeType => ExpressionType.Extension;

    /// <summary>
    /// Gets the field return type.
    /// </summary>
    public override Type Type { get; }

    /// <summary>
    /// Gets the type the field is a member of.
    /// </summary>
    public Type DeclaringType { get; }
}
