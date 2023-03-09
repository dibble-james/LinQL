namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// An expression that defines access to a field that can have child fields.
/// </summary>
public class TypeFieldExpression : FieldExpression
{
    private Dictionary<string, Expression> fields = new();
    private Dictionary<string, string> arguments = new();

    /// <summary>
    /// Create a new <see cref="TypeFieldExpression"/>.
    /// </summary>
    /// <param name="field">The field name.</param>
    /// <param name="fieldType">The return type of the field.</param>
    /// <param name="declaringType">The .Net type that field is a member of.</param>
    /// <param name="root"></param>
    public TypeFieldExpression(string field, Type fieldType, Type declaringType, IRootExpression? root)
        : base(field, fieldType, declaringType) => this.Root = root ?? (this as IRootExpression)!;

    /// <inheritdoc />
    protected TypeFieldExpression(string field, Type fieldType, Type declaringType)
        : this(field, fieldType, declaringType, null)
    {
    }

    /// <summary>
    /// Gets the arguments to be passed to the field on the server.
    /// </summary>
    public IReadOnlyDictionary<string, string> Arguments => this.arguments;

    /// <summary>
    /// Gets the root expression
    /// </summary>
    public IRootExpression Root { get; }

    /// <inheritdoc/>
    public FieldExpression WithField(FieldExpression field)
        => this.fields.GetOrAdd(field.FieldName, () => field);

    /// <summary>
    /// Add an argument to the field.
    /// </summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="type">The graphql type.</param>
    /// <param name="value">The name of the variable that will hold the argument value.</param>
    /// <returns>The updated <see cref="TypeFieldExpression"/>.</returns>
    public TypeFieldExpression WithArgument(string name, Type type, object? value)
    {
        var variable = this.Root.WithVariable(type, value);

        this.arguments.Add(name, variable.Name);
        return this;
    }

    internal TypeFieldExpression ReplaceType(Type type)
        => new(this.FieldName, type, this.DeclaringType, this.Root)
        {
            fields = this.fields,
            arguments = this.arguments,
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
