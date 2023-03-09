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
    /// Register a required variable
    /// </summary>
    /// <param name="type">The variable type</param>
    /// <param name="value">The variable value</param>
    /// <returns>The variable</returns>
    Variable WithVariable(Type type, object? value);
}

/// <summary>
/// An <see cref="Expression"/> representation of a GraphQL query.
/// </summary>
/// <typeparam name="TRoot">The root query type.</typeparam>
/// <typeparam name="TData">The query result type.</typeparam>
public class GraphQLExpression<TRoot, TData> : TypeFieldExpression, IRootExpression
{
    private readonly List<Variable> variables = new();
    private Lazy<string> queryValue;

    /// <summary>
    /// Creates a new GraphQL Expression.
    /// </summary>
    /// <param name="rootOperation">The root operation to query against.</param>
    /// <param name="originalQuery">The expression this will be based on.</param>
    public GraphQLExpression(RootOperation rootOperation, Expression<Func<TRoot, TData>> originalQuery)
        : base(rootOperation.Name, typeof(TData), typeof(TRoot), null)
    {
        (this.RootOperation, this.OriginalQuery) = (rootOperation, originalQuery);
        this.queryValue = this.ToStringInternal();
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
    public Variable WithVariable(Type type, object? value)
    {
        var variableName = $"var{this.variables.Count + 1}";

        var variable = new Variable(variableName, "Any", value);

        this.variables.Add(variable);

        return variable;
    }

    /// <inheritdoc/>
    public override string ToString() => this.queryValue.Value;

    private Lazy<string> ToStringInternal()
        => new(() => new GraphQLExpressionTranslator<TRoot, TData>().Translate(this));
}
