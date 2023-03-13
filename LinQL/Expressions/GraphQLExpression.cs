namespace LinQL.Expressions;

using System.Linq.Expressions;
using LinQL.Description;
using LinQL.Translation;

/// <summary>
/// Defines the root expression.
/// </summary>
public interface IRootExpression
{
    /// <summary>
    /// Gets the variables discovered in the query.
    /// </summary>
    IReadOnlyCollection<Variable> Variables { get; }

    /// <summary>
    /// Gets the scalar types discovered in the schema
    /// </summary>
    IReadOnlyCollection<Scalar> Scalars { get; }

    /// <summary>
    /// Register a required variable
    /// </summary>
    /// <param name="type">The variable type</param>
    /// <param name="value">The variable value</param>
    /// <returns>The variable</returns>
    Variable WithVariable(string type, object? value);
}

/// <summary>
/// An <see cref="Expression"/> representation of a GraphQL query.
/// </summary>
/// <typeparam name="TRoot">The root query type.</typeparam>
/// <typeparam name="TData">The query result type.</typeparam>
public class GraphQLExpression<TRoot, TData> : TypeFieldExpression, IRootExpression
    where TRoot : RootType<TRoot>
{
    private readonly List<Variable> variables = new();
    private readonly LinQLOptions options;
    private Lazy<string> queryValue;

    /// <summary>
    /// Creates a new GraphQL Expression.
    /// </summary>
    /// <param name="rootOperation">The root operation to query against.</param>
    /// <param name="originalQuery">The expression this will be based on.</param>
    /// <param name="options">The current configuration</param>
    public GraphQLExpression(RootOperation rootOperation, Expression<Func<TRoot, TData>> originalQuery, LinQLOptions options)
        : base(rootOperation.Name, typeof(TData), typeof(TRoot), null)
    {
        (this.RootOperation, this.OriginalQuery) = (rootOperation, originalQuery);
        this.queryValue = this.ToStringInternal();
        this.options = options;
    }

    /// <summary>
    /// Gets the root operation.
    /// </summary>
    public RootOperation RootOperation { get; }

    /// <summary>
    /// Gets the expressions this expression was built from.
    /// </summary>
    public Expression<Func<TRoot, TData>> OriginalQuery { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<Variable> Variables => this.variables;

    /// <inheritdoc />
    public IReadOnlyCollection<Scalar> Scalars => this.options.Scalars.ToList();

    /// <summary>
    /// Add an extra field to the selection.
    /// </summary>
    /// <param name="include">The field to include.</param>
    /// <returns>The translated expression.</returns>
    public GraphQLExpression<TRoot, TData> Include(Expression<Func<TRoot, object>> include)
    {
        this.queryValue = this.ToStringInternal();
        return ExpressionTranslator.Include(this, include);
    }

    /// <inheritdoc/>
    public Variable WithVariable(string type, object? value)
    {
        var variableName = $"var{this.variables.Count + 1}";

        var variable = new Variable(variableName, type, value);

        this.variables.Add(variable);

        return variable;
    }

    /// <inheritdoc/>
    public override string ToString() => this.queryValue.Value;

    private Lazy<string> ToStringInternal()
        => new(() => new GraphQLExpressionTranslator<TRoot, TData>().Translate(this));
}
