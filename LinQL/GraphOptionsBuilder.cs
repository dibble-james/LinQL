namespace LinQL;

using System.Net.WebSockets;
using System.Text.Json;
using LinQL.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>
/// Builder of <see cref="Graph"/> configuration.
/// </summary>
/// <typeparam name="TGraph">The graph being built.</typeparam>
public class GraphOptionsBuilder<TGraph>
    where TGraph : Graph
{
    private readonly OptionsBuilder<GraphOptions<TGraph>> options;

    /// <summary>
    /// Create a new <see cref="GraphOptionsBuilder{TGraph}"/>.
    /// </summary>
    /// <param name="services">The service collection to add the graph too.</param>
    public GraphOptionsBuilder(IServiceCollection services)
    {
        this.Services = services;
        services.AddTransient<TGraph>();
        services.AddSingleton<IQueryTranslator, TranslationProvider>();

        this.options = services.AddOptions<GraphOptions<TGraph>>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<GraphOptions<TGraph>>, GraphOptions<TGraph>.Validator>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<GraphOptions<TGraph>>>().Value);
    }

    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }

    /// <summary>
    /// Setup how to deserialize server responses.
    /// </summary>
    /// <param name="serialisation">Setup JSON serialisation.</param>
    /// <returns>This builder.</returns>
    public GraphOptionsBuilder<TGraph> ConfigureSerialization(Action<JsonSerializerOptions> serialisation)
    {
        this.options.Configure(opt => serialisation(opt.Serializer));
        return this;
    }

    /// <summary>
    /// Register an interface type.
    /// </summary>
    /// <typeparam name="T">The interface type to recognize and deserialize too.</typeparam>
    /// <param name="knownTypes">Known implementations of the interface.</param>
    /// <returns>This builder.</returns>
    public GraphOptionsBuilder<TGraph> AddInterfaceType<T>(params Type[] knownTypes)
    {
        this.options.Configure(opt => opt.Serializer.Converters.Add(new InterfaceJsonDeserializer<T>(knownTypes)));
        return this;
    }

    /// <summary>
    /// Use an <see cref="HttpClient"/> to access the GraphQL server.
    /// </summary>
    /// <param name="configure">Setup for the <see cref="HttpClient"/>.</param>
    /// <returns>This builder.</returns>
    public GraphOptionsBuilder<TGraph> WithHttpConnection(Action<HttpClient> configure)
    {
        this.Services.AddHttpClient(typeof(TGraph).Name)
            .ConfigureHttpClient(configure);

        this.options.Configure<IHttpClientFactory>(
            (opt, connection) => opt.Connection = () => new HttpGraphQLConnection(
                connection.CreateClient(typeof(TGraph).Name),
                opt.Serializer));

        return this;
    }

    /// <summary>
    /// Use a <see cref="ClientWebSocket"/> to handle supscrption requests.
    /// </summary>
    /// <param name="uri">The subscription server.</param>
    /// <param name="configure">Any required configuration for the <see cref="ClientWebSocket"/>.</param>
    /// <returns>This builder.</returns>
    public GraphOptionsBuilder<TGraph> WithWebSocketConnection(Uri uri, Action<ClientWebSocket> configure)
    {
        this.options.Configure(
            opt => opt.SubscriptionConnection = () =>
            {
                var client = new ClientWebSocket();
                configure(client);
                return new WebsocketSubscrptionConnection(new Websocket.Client.WebsocketClient(uri, () => client), opt.Serializer);
            });

        return this;
    }
}
