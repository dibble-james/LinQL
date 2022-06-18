namespace LinQL;

using LinQL.Translation;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Builder of <see cref="Graph"/> configuration.
/// </summary>
/// <typeparam name="TGraph">The graph being built.</typeparam>
public class GraphBuilder<TGraph>
    where TGraph : Graph
{
    /// <summary>
    /// Create a new <see cref="GraphBuilder{TGraph}"/>.
    /// </summary>
    /// <param name="services">The service collection to add the graph too.</param>
    public GraphBuilder(IServiceCollection services)
    {
        this.Services = services;
        services.AddTransient<TGraph>();
        services.AddSingleton<IQueryTranslator, TranslationProvider>();
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Use an <see cref="HttpClient"/> to access the GraphQL server.
    /// </summary>
    /// <param name="configure">Setup for the <see cref="HttpClient"/>.</param>
    /// <returns>This builder.</returns>
    public GraphBuilder<TGraph> WithHttpConnection(Action<HttpClient> configure)
    {
        this.Services.AddHttpClient(typeof(TGraph).Name)
            .ConfigureHttpClient(configure);

        this.Services.AddTransient<IGraphQLConnection, HttpGraphQLConnection>(
            sp => new HttpGraphQLConnection(sp.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(TGraph).Name)));

        return this;
    }
}
