namespace LinQL;

/// <summary>
/// How to communicate with the GraphQL server.
/// </summary>
public interface IGraphQLConnection
{
    /// <summary>
    /// Send the request to the server.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server response.</returns>
    Task<GraphQLResponse<T?>> SendRequest<T>(GraphQLRequest request, CancellationToken cancellationToken);
}
