namespace LinQL;

/// <summary>
/// Handles a message returned from a subscription request.
/// </summary>
/// <typeparam name="T">The subscription result.</typeparam>
/// <param name="message">The data from the server.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns></returns>
public delegate Task OnSubscriptionMessage<T>(GraphQLResponse<T?> message, CancellationToken cancellationToken);

/// <summary>
/// A transport that supports subscription requests.
/// </summary>
public interface IGraphQLSubscriptionConnection
{
    /// <summary>
    /// Start listening for subscription results.
    /// </summary>
    /// <typeparam name="TResult">The subscription result.</typeparam>
    /// <param name="request">The subscription request.</param>
    /// <param name="handler">A subscription result handler.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A handle on the subscription.</returns>
    Task<IDisposable> Subscribe<TResult>(GraphQLRequest request, OnSubscriptionMessage<TResult> handler, CancellationToken cancellationToken = default);
}
