namespace LinQL;

using System.Linq.Expressions;
using FastExpressionCompiler;
using LinQL.Description;

public static class GraphQLExpressionRequestExtentions
{
    /// <summary>
    /// Execute the query and get the server response.
    /// </summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    public static async Task<GraphQLResponse<TData>> SendAsync<TRoot, TData>(
        this GraphQLExpressionRequest<TRoot, TData> request,
        CancellationToken cancellationToken = default)
    {
        var response = await (request.Expression.RootOperation switch
        {
            { Type: RootOperationType.Query } => request.Graph.GraphQLClient.SendQueryAsync<TRoot>(request, cancellationToken),
            { Type: RootOperationType.Mutation } => request.Graph.GraphQLClient.SendMutationAsync<TRoot>(request, cancellationToken),
            _ => throw new NotSupportedException(),
        }).ConfigureAwait(false);

        var result = request.Expression.OriginalQuery.CompileFast()(response.Data);

        return new GraphQLResponse<TData> { Data = result, Errors = response.Errors, Extensions = response.Extensions };
    }

    public static GraphQLExpressionRequest<TRoot, TData> Include<TRoot, TData>(
        this GraphQLExpressionRequest<TRoot, TData> request,
        Expression<Func<TRoot, object>> include)
    {
        request.Expression.Include(include);

        return request;
    }
}
