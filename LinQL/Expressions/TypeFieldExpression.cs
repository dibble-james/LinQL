namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// An expression that defines access to a field that can have child fields.
/// </summary>
/// <remarks>
/// Create a new <see cref="TypeFieldExpression"/>.
/// </remarks>
/// <param name="field">The field name.</param>
/// <param name="fieldType">The return type of the field.</param>
/// <param name="declaringType">The .Net type that field is a member of.</param>
/// <param name="root"></param>
public class TypeFieldExpression(string field, Type fieldType, Type declaringType, IRootExpression? root)
 : FieldExpression(field, fieldType, declaringType, root)
{
    private Dictionary<string, Expression> fields = [];

    /// <inheritdoc/>
    public FieldExpression WithField(FieldExpression field)
        => this.fields.GetOrAdd(field.FieldName, () => field);

    internal TypeFieldExpression ReplaceType(Type type)
        => new(this.FieldName, type, this.DeclaringType, this.Root)
        {
            fields = this.fields,
            Arguments = this.Arguments,
        };

    /// <inheritdoc />
    protected override Expression VisitChildren(ExpressionVisitor visitor)
    {
        if (this.fields.Any())
        {
            foreach (var field in this.fields.Values)
            {
                visitor.Visit(field);
            }
        }
        else
        {
            foreach (var scalar in this.Type.GetAllScalars(this.Root))
            {
                visitor.Visit(scalar);
            }
        }

        return this;
    }
}
