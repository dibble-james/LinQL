namespace LinQL.GraphQL.Client;

using global::GraphQL;

/// <summary>
/// A <see cref="GraphQLResponse{TData}"/> for a declared query.
/// </summary>
/// <typeparam name="TData">The projected result type.</typeparam>
public class LinQLQueryResponse<TData> : GraphQLResponse<TData>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinQLQueryResponse{TData}"/> class.
    /// </summary>
    /// <param name="document">The document that was sent.</param>
    public LinQLQueryResponse(string document) => this.Document = document;

    /// <summary>
    /// Gets the document that was sent.
    /// </summary>
    public string Document { get; }
}
