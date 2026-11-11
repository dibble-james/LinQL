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
        IReadOnlyCollection<Variable> variables)
    {
        var document = new LinQLDocument(operationType, query);

        var values = variables.Count == 0
            ? null
            : variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        return new OperationRequest(OperationName, document, values);
    }
}
