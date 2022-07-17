namespace LinQL.Websockets;

/// <summary>
/// Websocket server response.
/// </summary>
/// <param name="Type">The response type.</param>
/// <param name="Payload">The data from the server.</param>
public record SubscriptionResponse(string Type, object? Payload)
{
    /// <summary>
    /// The values for <see cref="Type"/>.
    /// </summary>
    public static class Types
    {
        /// <summary>
        /// Response with expected data.
        /// </summary>
        public const string Next = "data";
    }
}

/// <summary>
/// Websocket server response.
/// </summary>
/// <param name="Type">The response type.</param>
/// <param name="Payload">The data from the server.</param>
public record SubscriptionResponse<TData>(string Type, GraphQLResponse<TData>? Payload);
