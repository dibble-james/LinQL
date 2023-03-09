namespace LinQL.GraphQL.Client;

using System;
using System.Threading;
using System.Threading.Tasks;
using global::GraphQL.Client.Abstractions;

/// <summary>
///  A wrapper for <see cref="IGraphQLClient"/> to contain LinQL configuration.
/// </summary>
public class LinqlGraphQLClient : IGraphQLClient
{
    private readonly IGraphQLClient client;

    /// <summary>
    /// Creates a new instance of the <see cref="LinqlGraphQLClient"/> class.
    /// </summary>
    /// <param name="client">The wrapped client.</param>
    /// <param name="options">LinQL configuration.</param>
    public LinqlGraphQLClient(IGraphQLClient client, LinqlOptions options)
        => (this.client, this.Options) = (client, options);

    /// <summary>
    /// Gets the configuration of this client.
    /// </summary>
    public LinqlOptions Options { get; }

    /// <inheritdoc/>
    public IObservable<global::GraphQL.GraphQLResponse<TResponse>> CreateSubscriptionStream<TResponse>(global::GraphQL.GraphQLRequest request) => this.client.CreateSubscriptionStream<TResponse>(request);

    /// <inheritdoc/>
    public IObservable<global::GraphQL.GraphQLResponse<TResponse>> CreateSubscriptionStream<TResponse>(global::GraphQL.GraphQLRequest request, Action<Exception> exceptionHandler) => this.client.CreateSubscriptionStream<TResponse>(request, exceptionHandler);

    /// <inheritdoc/>
    public Task<global::GraphQL.GraphQLResponse<TResponse>> SendMutationAsync<TResponse>(global::GraphQL.GraphQLRequest request, CancellationToken cancellationToken = default) => this.client.SendMutationAsync<TResponse>(request, cancellationToken);

    /// <inheritdoc/>
    public Task<global::GraphQL.GraphQLResponse<TResponse>> SendQueryAsync<TResponse>(global::GraphQL.GraphQLRequest request, CancellationToken cancellationToken = default) => this.client.SendQueryAsync<TResponse>(request, cancellationToken);
}
