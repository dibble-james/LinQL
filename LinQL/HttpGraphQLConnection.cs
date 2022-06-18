namespace LinQL;

using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

internal class HttpGraphQLConnection : IGraphQLConnection
{
    private readonly HttpClient httpClient;

    public HttpGraphQLConnection(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<GraphQLResponse<T>> SendRequest<T>(GraphQLRequest request, CancellationToken cancellationToken)
    {
        var httpRequest = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            Content = JsonContent.Create(request)
        };

        var httpResponse = await this.httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var response = await httpResponse.Content.ReadFromJsonAsync<GraphQLResponse<T>>(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            throw new InvalidOperationException("Server responded with invalid json.");
        }

        return response;
    }
}
