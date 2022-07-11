namespace LinQL;

using Websocket.Client;

internal class WebsocketSubscrptionConnection : IGraphQLSubscriptionConnection
{
    private readonly WebsocketClient client;

    public WebsocketSubscrptionConnection(WebsocketClient client) => this.client = client;

    public Task<IDisposable> Subscribe<TResult>(GraphQLRequest request, OnSubscriptionMessage<TResult> handler, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
