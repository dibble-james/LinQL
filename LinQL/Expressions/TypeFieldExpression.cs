namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// An expression that defines access to a field that can have child fields.
/// </summary>
public class TypeFieldExpression : FieldExpression
{
    private Dictionary<string, Expression> fields = new();
    private Dictionary<string, object?> arguments = new();

    /// <summary>
    /// Create a new <see cref="TypeFieldExpression"/>.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="fieldType">The return type of the field.</param>
    /// <param name="declaringType">The .Net type that field is a member of.</param>
    public TypeFieldExpression(string field, Type fieldType, Type declaringType)
        : base(field, fieldType, declaringType) { }

    /// <summary>
    /// Gets the arguments to be passed to the field on the server.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Arguments => this.arguments;

    /// <inheritdoc/>
    public FieldExpression WithField(FieldExpression field)
        => this.fields.GetOrAdd(field.FieldName, () => field);

    /// <summary>
    /// Add an argument to the field.
    /// </summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="argument">The argument value.</param>
    /// <returns>The updated <see cref="TypeFieldExpression"/>.</returns>
    public TypeFieldExpression WithArgument(string name, object? argument)
    {
        this.arguments.Add(name, argument);
        return this;
    }

    internal TypeFieldExpression ReplaceType(Type type)
        => new(this.FieldName, type, this.DeclaringType)
        {
            fields = this.fields,
            arguments = this.arguments
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
            foreach (var scalar in this.Type.GetAllScalars())
            {
                visitor.Visit(scalar);
            }
        }

        return this;
    }
}
