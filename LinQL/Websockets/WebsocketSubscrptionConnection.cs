namespace LinQL.Websockets;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Websocket.Client;

internal class WebsocketSubscrptionConnection : IGraphQLSubscriptionConnection
{
    private readonly WebsocketClient client;
    private readonly JsonSerializerOptions serializerOptions;

    public WebsocketSubscrptionConnection(WebsocketClient client, JsonSerializerOptions serializerOptions)
        => (this.client, this.serializerOptions) = (client, serializerOptions);

    public async Task<IDisposable> Subscribe<TResult>(GraphQLRequest request, OnSubscriptionMessage<TResult> handler, Func<CancellationToken, Task>? subscriptionEnded = null, CancellationToken cancellationToken = default)
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

        return this.client.MessageReceived.Subscribe(this.OnMessage(handler, subscriptionEnded, cancellationToken));
    }

    public async IAsyncEnumerable<GraphQLResponse<TResult?>> Subscribe<TResult>(GraphQLRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<GraphQLResponse<TResult?>>();

        Task OnMessage(GraphQLResponse<TResult?> result, CancellationToken ct)
        {
            tcs!.SetResult(result);
            return Task.CompletedTask;
        }

        Task OnComplete(CancellationToken ct)
        {
            tcs.SetCanceled();
            return Task.CompletedTask;
        }

        await this.Subscribe<TResult>(request, OnMessage, OnComplete, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await tcs.Task;
            tcs = new TaskCompletionSource<GraphQLResponse<TResult?>>();
        }
    }

    private Action<ResponseMessage> OnMessage<TResult>(OnSubscriptionMessage<TResult> handler, Func<CancellationToken, Task>? subscriptionEnded, CancellationToken cancellationToken) => async result =>
    {
        var response = JsonSerializer.Deserialize<SubscriptionResponse>(result.Text, this.serializerOptions);

        switch (response?.Type)
        {
            case SubscriptionResponse.Types.Complete:
            {
                subscriptionEnded?.Invoke(cancellationToken);
                return;
            }
            case SubscriptionResponse.Types.Data:
            {
                var data = JsonSerializer.Deserialize<SubscriptionResponse<TResult?>>(result.Text, this.serializerOptions);

                if (data?.Payload is null)
                {
                    return;
                }

                await handler(data.Payload, cancellationToken);

                return;
            }
            default:
                return;
        }
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
