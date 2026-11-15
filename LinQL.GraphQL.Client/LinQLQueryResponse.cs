namespace LinQL.GraphQL.Client;

using global::GraphQL;

/// <summary>
/// A <see cref="GraphQLResponse{TData}"/> for a declared query.
/// </summary>
/// <typeparam name="TData">The projected result type.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="LinQLQueryResponse{TData}"/> class.
/// </remarks>
/// <param name="document">The document that was sent.</param>
public class LinQLQueryResponse<TData>(string document) : GraphQLResponse<TData>
{

    /// <summary>
    /// Gets the document that was sent.
    /// </summary>
    public string Document { get; } = document;
}
