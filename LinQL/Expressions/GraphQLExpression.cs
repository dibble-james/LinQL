namespace LinQL.Expressions;

using System.Linq.Expressions;
using LinQL.Description;
using LinQL.Translation;

/// <summary>
/// An <see cref="Expression"/> representation of a GraphQL query.
/// </summary>
/// <typeparam name="TRoot">The root query type.</typeparam>
/// <typeparam name="TResult">The query result type.</typeparam>
public class GraphQLExpression<TRoot, TResult> : TypeFieldExpression
{
    /// <param name="rootOperation">The root operation to query against.</param>
    /// <param name="originalQuery">The expression this will be based on.</param>
    public GraphQLExpression(RootOperation rootOperation, Expression<Func<TRoot, TResult>> originalQuery)
        : base(rootOperation.Name, typeof(TResult), typeof(TRoot))
        => (this.RootOperation, this.OriginalQuery) = (rootOperation, originalQuery);

    /// <summary>
    /// Gets the root operation.
    /// </summary>
    public RootOperation RootOperation { get; }

    /// <summary>
    /// Gets the expressions this expression was built from.
    /// </summary>
    public Expression<Func<TRoot, TResult>> OriginalQuery { get; }

    /// <summary>
    /// Add an extra field to the selection.
    /// </summary>
    /// <param name="include">The field to include.</param>
    /// <returns>The translated expression.</returns>
    public GraphQLExpression<TRoot, TResult> Include(Expression<Func<TRoot, object>> include)
        => ExpressionTranslator.Include(this, include);
}
