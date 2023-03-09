
namespace GraphQL.Client.Abstractions;

using System.Linq.Expressions;
using System.Net.WebSockets;
using System.Reactive.Linq;
using FastExpressionCompiler;
using LinQL;
using LinQL.Description;
using LinQL.Expressions;
using LinQL.GraphQL.Client;
using LinQL.Translation;

/// <summary>
/// Methods for sending expressions to the server.
/// </summary>
public static class GraphQLClientExtensions
{
    /// <summary>
    /// Execute the query and get the server response.
    /// </summary>
    /// <param name="client">The GraphQL Client.</param>
    /// <param name="request">the GraphQL request for this subscription</param>
    /// <param name="includes">Any other things to execute against the query expression.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    public static async Task<GraphQLExpressionResponse<TRoot, TData>> SendAsync<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null,
        CancellationToken cancellationToken = default)
        where TRoot : RootType<TRoot>
    {
        var linqlClient = client as LinqlGraphQLClient ?? throw new InvalidOperationException($"Client was not a {nameof(LinqlGraphQLClient)}");

        var requestExpression = request.ToRequest(linqlClient.Options, includes).ToGraphQLClientRequest();

        var response = await (requestExpression.Expression.RootOperation switch
        {
            { Type: RootOperationType.Query } => client.SendQueryAsync<TRoot>(requestExpression, cancellationToken),
            { Type: RootOperationType.Mutation } => client.SendMutationAsync<TRoot>(requestExpression, cancellationToken),
            _ => throw new NotSupportedException($"Can't Send a {requestExpression.Expression.RootOperation.Type}"),
        }).ConfigureAwait(false);

        if (response.Data is null)
        {
            return new GraphQLExpressionResponse<TRoot, TData>(requestExpression.Expression) { Data = default!, Errors = response.Errors, Extensions = response.Extensions };
        }

        var result = requestExpression.Expression.OriginalQuery.CompileFast()(response.Data);

        return new GraphQLExpressionResponse<TRoot, TData>(requestExpression.Expression) { Data = result, Errors = response.Errors, Extensions = response.Extensions };
    }

    /// <summary>
    /// Creates a subscription to a GraphQL server. The connection is not established until the first actual subscription is made.<br/>
    /// All subscriptions made to this stream share the same hot observable.<br/>
    /// The stream must be recreated completely after an error has occurred within its logic (i.e. a <see cref="WebSocketException"/>)
    /// </summary>
    /// <param name="client">The GraphQL Client.</param>
    /// <param name="request">the GraphQL request for this subscription</param>
    /// <param name="includes">Any other things to execute against the query expression.</param>
    /// <returns>an observable stream for the specified subscription</returns>
    public static IObservable<GraphQLExpressionResponse<TRoot, TData>> CreateSubscriptionStream<TRoot, TData>(
        this IGraphQLClient client,
        Expression<Func<TRoot, TData>> request,
        Action<GraphQLExpression<TRoot, TData>>? includes = null)
        where TRoot : RootType<TRoot>
    {
        var linqlClient = client as LinqlGraphQLClient ?? throw new InvalidOperationException($"Client was not a {nameof(LinqlGraphQLClient)}");

        var requestExpression = request.ToRequest(linqlClient.Options, includes).ToGraphQLClientRequest();

        var subscription = requestExpression.Expression.RootOperation switch
        {
            { Type: RootOperationType.Subscription } => client.CreateSubscriptionStream<TRoot>(requestExpression),
            _ => throw new NotSupportedException($"Can't Subscribe to a {requestExpression.Expression.RootOperation.Type}"),
        };

        var selector = requestExpression.Expression.OriginalQuery.CompileFast();

        return subscription.Select(response => new GraphQLExpressionResponse<TRoot, TData>(requestExpression.Expression)
        {
            Data = selector(response.Data),
            Errors = response.Errors,
            Extensions = response.Extensions,
        });
    }
}
