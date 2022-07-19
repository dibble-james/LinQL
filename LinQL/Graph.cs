namespace LinQL;

using GraphQL.Client.Abstractions;

/// <summary>
/// The base class for defining GraphQL graphs.
/// </summary>
public abstract class Graph
{
    /// <summary>
    /// Create a new Graph.
    /// </summary>
    /// <param name="graphQLClient">Access to a server.</param>
    protected Graph(IGraphQLClient graphQLClient)
        => this.GraphQLClient = graphQLClient;

    /// <summary>
    /// Gets the <see cref="IGraphQLClient"/> used by this graph.
    /// </summary>
    public virtual IGraphQLClient GraphQLClient { get; }

    /// <summary>
    /// Build a <see cref="RootType{T}"/> for use on this graph.
    /// </summary>
    /// <typeparam name="T">The root type type.</typeparam>
    /// <returns>The root type.</returns>
    protected T RootType<T>()
        where T : RootType<T>, new() => new() { Graph = this };
}
