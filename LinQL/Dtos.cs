namespace LinQL;

using OneOf;

/// <summary>
/// Details of a request to a GraphQL server.
/// </summary>
/// <param name="Query">The query string.</param>
/// <param name="Variables">Any query variables.</param>
public record GraphQLRequest(string Query, IReadOnlyDictionary<string, object>? Variables = null);

/// <summary>
/// Details of a response from a GraphQL server.
/// </summary>
/// <typeparam name="T">The data type.</typeparam>
/// <param name="Data">The response from the server.</param>
/// <param name="Errors">Any query errors.</param>
public record GraphQLResponse<T>(T? Data, IEnumerable<GraphQLError>? Errors)
{
    /// <summary>
    /// Gets the request that generated this response.
    /// </summary>
    public GraphQLRequest Request { get; init; } = default!;

    /// <summary>
    /// Railway-style check for errors from the response.
    /// </summary>
    /// <returns>Either the errors if any are present or the data.</returns>
    public OneOf<IEnumerable<GraphQLError>, T> HasErrors()
        => this.Errors?.Any() == true
        ? OneOf<IEnumerable<GraphQLError>, T>.FromT0(this.Errors ?? Enumerable.Empty<GraphQLError>())
        : OneOf<IEnumerable<GraphQLError>, T>.FromT1(this.Data!);

    /// <summary>
    /// Check if any errors are present and throw if there are.
    /// </summary>
    /// <returns>The data if no errors are present.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when any errors are present.  Contains an <see cref="AggregateException"/> with all the other errors.
    /// </exception>
    public T EnsureSuccessfulResponse()
    {
        if (this.Errors?.Any() == true)
        {
            throw new InvalidOperationException(
                "GraphQL Response contained errors",
                new AggregateException(this.Errors.Select(e => new InvalidOperationException(e.Message))));
        }

        return this.Data!;
    }
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
