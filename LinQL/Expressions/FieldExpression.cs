namespace LinQL.Expressions;

using System.Linq.Expressions;

/// <summary>
/// A query of a field.
/// </summary>
public abstract class FieldExpression : Expression
{
    private Dictionary<string, string> arguments = [];

    /// <param name="field">The name of the field.</param>
    /// <param name="fieldType">The .Net return type of the field.</param>
    /// <param name="declaringType">The .Net type that field is a member of.</param>
    /// <param name="root">The root operation expression.</param>
    protected FieldExpression(string field, Type fieldType, Type declaringType, IRootExpression? root)
        => (this.FieldName, this.Type, this.DeclaringType, this.Root) = (field, fieldType, declaringType, root ?? (this as IRootExpression)!);

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

    /// <summary>
    /// Gets the arguments to be passed to the field on the server.
    /// </summary>
    public IReadOnlyDictionary<string, string> Arguments
    {
        get => this.arguments;
        protected set => this.arguments = value.ToDictionary(x => x.Key, x => x.Value);
    }

    /// <summary>
    /// Gets the root expression
    /// </summary>
    public IRootExpression Root { get; }

    /// <summary>
    /// Add an argument to the field.
    /// </summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="type">The graphql type.</param>
    /// <param name="value">The name of the variable that will hold the argument value.</param>
    /// <returns>The updated <see cref="FieldExpression"/>.</returns>
    public FieldExpression WithArgument(string name, string type, object? value)
    {
        var variable = this.Root.WithVariable(type, value);

        this.arguments.Add(name, variable.Name);
        return this;
    }
}
