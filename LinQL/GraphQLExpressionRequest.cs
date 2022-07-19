namespace LinQL;

using LinQL.Expressions;

public class GraphQLExpressionRequest<TRoot, TData> : GraphQLRequest
{
    public GraphQLExpressionRequest(GraphQLExpression<TRoot, TData> expression, Graph graph)
    {
        this.Expression = expression;
        this.Graph = graph;
    }

    public GraphQLExpression<TRoot, TData> Expression { get; }

    public Graph Graph { get; }
}
