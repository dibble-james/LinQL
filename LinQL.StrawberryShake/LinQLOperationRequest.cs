namespace LinQL.StrawberryShake;

using global::StrawberryShake;
using LinQL.Description;
using LinQL.Expressions;

// Builds Strawberry Shake OperationRequests from translated LinQL queries.
internal static class LinQLOperationRequest
{
    /// <summary>
    /// The operation name the LinQL translator declares on every document.
    /// </summary>
    public const string OperationName = "linql";

    /// <summary>
    /// Creates an <see cref="OperationRequest"/> for a translated LinQL query.
    /// </summary>
    /// <param name="operationType">The root operation type.</param>
    /// <param name="query">The translated query.</param>
    /// <param name="variables">The variables discovered while translating.</param>
    /// <returns>The operation request.</returns>
    public static OperationRequest Create(
        RootOperationType operationType,
        string query,
        IReadOnlyCollection<Variable> variables) => Create(
            operationType,
            query,
            variables.ToDictionary(variable => variable.Name, variable => variable.Value));

    /// <summary>
    /// Creates an <see cref="OperationRequest"/> for a declared query's document.
    /// </summary>
    /// <param name="operationType">The root operation type.</param>
    /// <param name="query">The document generated at build time.</param>
    /// <param name="variables">The values the document's variables are bound to.</param>
    /// <param name="id">
    /// The persisted operation id to send instead of the document, or <see langword="null"/> to send the document.
    /// </param>
    /// <returns>The operation request.</returns>
    /// <remarks>
    /// Strawberry Shake's transport sends the id in place of the query text when one is given, which is how a
    /// persisted operation is executed against a server that already holds the document.
    /// </remarks>
    public static OperationRequest Create(
        RootOperationType operationType,
        string query,
        IReadOnlyDictionary<string, object?> variables,
        string? id = null)
    {
        var document = new LinQLDocument(operationType, query);
        var values = variables.Count == 0 ? null : variables;

        return id is null
            ? new OperationRequest(OperationName, document, values)
            : new OperationRequest(id, OperationName, document, values);
    }
}
