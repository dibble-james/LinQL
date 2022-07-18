namespace LinQL;

using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

internal class HttpGraphQLConnection : IGraphQLConnection
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions serializerOptions;

    public HttpGraphQLConnection(HttpClient httpClient, JsonSerializerOptions serializerOptions)
        => (this.httpClient, this.serializerOptions) = (httpClient, serializerOptions);

    public async Task<GraphQLResponse<T>> SendRequest<T>(GraphQLRequest request, CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            Content = JsonContent.Create(request, null, this.serializerOptions)
        };

        var httpResponse = await this.httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var response = await httpResponse.Content.ReadFromJsonAsync<GraphQLResponse<T>>(this.serializerOptions, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            throw new InvalidOperationException("Server responded with invalid json.");
        }

        return response with { Request = request };
    }
}
