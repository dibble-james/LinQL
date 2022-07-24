namespace LinQL.Expressions;

using System.Linq.Expressions;
using LinQL.Description;
using LinQL.Translation;

/// <summary>
/// An <see cref="Expression"/> representation of a GraphQL query.
/// </summary>
/// <typeparam name="TRoot">The root query type.</typeparam>
/// <typeparam name="TData">The query result type.</typeparam>
public class GraphQLExpression<TRoot, TData> : TypeFieldExpression
{
    private Lazy<string> queryValue;

    /// <summary>
    /// Creates a new GraphQL Expression.
    /// </summary>
    /// <param name="rootOperation">The root operation to query against.</param>
    /// <param name="originalQuery">The expression this will be based on.</param>
    public GraphQLExpression(RootOperation rootOperation, Expression<Func<TRoot, TData>> originalQuery)
        : base(rootOperation.Name, typeof(TData), typeof(TRoot))
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
    public override string ToString() => this.queryValue.Value;

    private Lazy<string> ToStringInternal()
        => new(() => new GraphQLExpressionTranslator<TRoot, TData>().Translate(this));
}
