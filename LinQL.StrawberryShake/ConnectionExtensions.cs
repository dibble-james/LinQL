namespace LinQL.StrawberryShake;

using System.Text.Json;
using global::StrawberryShake;

/// <summary>
/// Methods for executing LinQL expressions over a Strawberry Shake connection.
/// </summary>
public static class ConnectionExtensions
{
    /// <summary>
    /// Wrap an <see cref="IConnection{TResponseBody}"/> with LinQL using the default options.
    /// </summary>
    /// <param name="connection">The connection to wrap.</param>
    /// <returns>The LinQL client.</returns>
    public static StrawberryShakeLinQLClient WithLinQL(this IConnection<JsonDocument> connection)
        => new(connection);

    /// <summary>
    /// Wrap an <see cref="IConnection{TResponseBody}"/> with LinQL.
    /// </summary>
    /// <param name="connection">The connection to wrap.</param>
    /// <param name="options">The LinQL options.</param>
    /// <returns>The LinQL client.</returns>
    public static StrawberryShakeLinQLClient WithLinQL(this IConnection<JsonDocument> connection, LinQLOptions options)
        => new(connection, new LinQLStrawberryShakeOptions { LinQL = options });

    /// <summary>
    /// Wrap an <see cref="IConnection{TResponseBody}"/> with LinQL.
    /// </summary>
    /// <param name="connection">The connection to wrap.</param>
    /// <param name="configure">Configures the client options.</param>
    /// <returns>The LinQL client.</returns>
    public static StrawberryShakeLinQLClient WithLinQL(
        this IConnection<JsonDocument> connection,
        Action<LinQLStrawberryShakeOptions> configure)
    {
        var options = new LinQLStrawberryShakeOptions();
        configure(options);

        return new StrawberryShakeLinQLClient(connection, options);
    }
}
