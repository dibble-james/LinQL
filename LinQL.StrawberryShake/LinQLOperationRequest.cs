namespace LinQL.StrawberryShake;

using global::StrawberryShake;
using LinQL.Description;
using LinQL.Expressions;

/// <summary>
/// Builds Strawberry Shake <see cref="OperationRequest"/>s from translated LinQL queries.
/// </summary>
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
    /// <returns>The operation request.</returns>
    public static OperationRequest Create(
        RootOperationType operationType,
        string query,
        IReadOnlyDictionary<string, object?> variables)
    {
        var document = new LinQLDocument(operationType, query);

        return new OperationRequest(OperationName, document, variables.Count == 0 ? null : variables);
    }
}
