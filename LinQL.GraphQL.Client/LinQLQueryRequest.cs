namespace LinQL.GraphQL.Client;

using System.Collections.Generic;
using global::GraphQL;

/// <summary>
/// A <see cref="GraphQLRequest"/> built from a declared query's generated document.
/// </summary>
/// <remarks>
/// A declared query has no <see cref="Expressions.GraphQLExpression{TRoot, TData}"/>, so the request that carries
/// one cannot be used for it; this carries the document and its bound values instead.
/// </remarks>
public class LinQLQueryRequest : GraphQLRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinQLQueryRequest"/> class.
    /// </summary>
    /// <param name="document">The document generated at build time.</param>
    /// <param name="variables">The values the document's variables are bound to.</param>
    public LinQLQueryRequest(string document, IReadOnlyDictionary<string, object?> variables)
        => (this.Query, this.Variables) = (document, variables);
}
