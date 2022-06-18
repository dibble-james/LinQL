namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// An expression that defines access to a field that can have child fields.
/// </summary>
public class TypeFieldExpression : FieldExpression, IHaveFields
{
    private readonly Dictionary<string, Expression> fields = new();
    private readonly Dictionary<string, object?> arguments = new();

    /// <summary>
    /// Create a new <see cref="TypeFieldExpression"/>.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="fieldType">The return type of the field.</param>
    public TypeFieldExpression(string field, Type fieldType)
        : base(field, fieldType) { }

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
            foreach (var scalar in this.Type.GetProperties().Where(p => p.PropertyType.IsScalar()))
            {
                visitor.Visit(scalar.ToField());
            }
        }

        return this;
    }
}
