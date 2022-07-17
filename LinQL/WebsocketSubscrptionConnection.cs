namespace LinQL;

using System.Text.Json;
using Websocket.Client;

internal class WebsocketSubscrptionConnection : IGraphQLSubscriptionConnection
{
    private readonly WebsocketClient client;
    private readonly JsonSerializerOptions serializerOptions;

    public WebsocketSubscrptionConnection(WebsocketClient client, JsonSerializerOptions serializerOptions)
        => (this.client, this.serializerOptions) = (client, serializerOptions);

    public async Task<IDisposable> Subscribe<TResult>(GraphQLRequest request, OnSubscriptionMessage<TResult> handler, CancellationToken cancellationToken = default)
    {
        await this.client.StartOrFail();

        await this.client.SendInstant(JsonSerializer.Serialize(request, this.serializerOptions));

        return this.client.MessageReceived.Subscribe(this.OnMessage(handler, cancellationToken));
    }

    private Action<ResponseMessage> OnMessage<TResult>(OnSubscriptionMessage<TResult> handler, CancellationToken cancellationToken) => async result =>
    {
        var response = JsonSerializer.Deserialize<GraphQLResponse<TResult>>(result.Text, this.serializerOptions);

        if (response is not null)
        {
            await handler(response.Data, cancellationToken);
        }
    };
}
