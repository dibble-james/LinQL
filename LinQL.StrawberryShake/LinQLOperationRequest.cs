namespace LinQL.StrawberryShake;

using LinQL.Description;
using LinQL.Expressions;
using global::StrawberryShake;

/// <summary>
/// Builds Strawberry Shake <see cref="OperationRequest"/>s from translated LinQL queries.
/// </summary>
internal static class LinQLOperationRequest
{
    /// <summary>
    /// Creates an <see cref="OperationRequest"/> for a translated LinQL query.
    /// </summary>
    /// <param name="operationType">The root operation type.</param>
    /// <param name="query">The translated query.</param>
    /// <param name="variables">The variables discovered while translating.</param>
    /// <returns>The operation request and the normalized query that was sent.</returns>
    public static (OperationRequest Request, string Query) Create(
        RootOperationType operationType,
        string query,
        IReadOnlyCollection<Variable> variables)
    {
        var (normalized, name) = LinQLDocument.Normalize(query);
        var document = new LinQLDocument(operationType, name, normalized);

        var values = variables.Count == 0
            ? null
            : variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        return (new OperationRequest(name, document, values), normalized);
    }
}
