namespace LinQL;

/// <summary>
/// Details of a request to a GraphQL server.
/// </summary>
/// <param name="Graph">The parent graph.</param>
/// <param name="Query">The query string.</param>
/// <param name="Variables">Any query variables.</param>
public record GraphQLRequest(Graph Graph, string Query, IReadOnlyDictionary<string, object>? Variables = null);

/// <summary>
/// Details of a response from a GraphQL server.
/// </summary>
/// <typeparam name="T">The data type.</typeparam>
/// <param name="Data">The response from the server.</param>
/// <param name="Errors">Any query errors.</param>
public record GraphQLResponse<T>(T Data, IEnumerable<GraphQLError> Errors)
{
    /// <summary>
    /// Gets the request that generated this response.
    /// </summary>
    public GraphQLRequest Request { get; init; } = default!;
}

/// <summary>
/// An error from a GraphQL server.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="Locations">Which part of the query failed.</param>
public record GraphQLError(string Message, IEnumerable<Location> Locations);

/// <summary>
/// The bit of the query that failed.
/// </summary>
/// <param name="Line">The query line.</param>
/// <param name="Column">The query column.</param>
public record Location(int Line, int Column);
