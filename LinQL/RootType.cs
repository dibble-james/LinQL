namespace LinQL;

using System.Linq.Expressions;
using System.Text.Json.Serialization;
using LinQL.Translation;

/// <summary>
/// LINQ acess to a root GraphQL type.
/// </summary>
/// <typeparam name="T">The root operation type.</typeparam>
public abstract class RootType<T>
    where T : new()
{
    /// <summary>
    /// Gets or sets the parent graph.
    /// </summary>
    [JsonIgnore]
    public Graph? Graph { get; internal set; }

    /// <summary>
    /// Build a query from a lambda expression.
    /// </summary>
    /// <typeparam name="TResult">The query result type.</typeparam>
    /// <param name="operation">The expression to query.</param>
    /// <returns>The translated expression.</returns>
    /// <exception cref="InvalidOperationException">Graph was not set.</exception>
    public GraphQLExpressionRequest<T, TResult> Select<TResult>(Expression<Func<T, TResult>> operation)
    {
        if (this.Graph is null)
        {
            throw new InvalidOperationException("Attempt to use a RootType not created by a Graph");
        }

        return operation.ToGraphQLRequest(this.Graph);
    }
}
