namespace LinQL.StrawberryShake.Tests;

using System.Text.Json;
using global::StrawberryShake;

/// <summary>
/// A connection that replays canned responses and records the requests it received.
/// </summary>
internal sealed class StubConnection : IConnection<JsonDocument>
{
    private readonly Queue<Response<JsonDocument>> responses;

    public StubConnection(params string[] json)
        => this.responses = new Queue<Response<JsonDocument>>(
            json.Select(document => new Response<JsonDocument>(JsonDocument.Parse(document), null)));

    public StubConnection(params Exception[] exceptions)
        => this.responses = new Queue<Response<JsonDocument>>(
            exceptions.Select(exception => new Response<JsonDocument>(null, exception)));

    public List<OperationRequest> Requests { get; } = [];

    public async IAsyncEnumerable<Response<JsonDocument>> ExecuteAsync(OperationRequest request)
    {
        this.Requests.Add(request);

        while (this.responses.Count > 0)
        {
            await Task.Yield();
            yield return this.responses.Dequeue();
        }
    }
}
