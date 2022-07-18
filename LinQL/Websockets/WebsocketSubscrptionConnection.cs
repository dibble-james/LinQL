namespace LinQL.Websockets;

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

        await this.client.SendInstant(
            JsonSerializer.Serialize(
                new SubscriptionRequest(SubscriptionRequest.Types.Init, null),
                this.serializerOptions));

        await this.client.SendInstant(
            JsonSerializer.Serialize(
                new SubscriptionRequest(SubscriptionRequest.Types.Subscribe, request),
                this.serializerOptions));

        return this.client.MessageReceived.Subscribe(this.OnMessage(handler, cancellationToken));
    }

    private Action<ResponseMessage> OnMessage<TResult>(OnSubscriptionMessage<TResult> handler, CancellationToken cancellationToken) => async result =>
    {
        var response = JsonSerializer.Deserialize<SubscriptionResponse>(result.Text, this.serializerOptions);

        if (response?.Type != SubscriptionResponse.Types.Data)
        {
            // Ignore other messages for now.
            return;
        }

        var data = JsonSerializer.Deserialize<SubscriptionResponse<TResult?>>(result.Text, this.serializerOptions);

        if (data?.Payload is null)
        {
            return;
        }

        await handler(data.Payload, cancellationToken);
    };


    private record SubscriptionRequest(string Type, GraphQLRequest? Payload)
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public static class Types
        {
            public const string Init = "connection_init";
            public const string Subscribe = "start";
        }
    }
}
