namespace LinQL.Expressions;

/// <summary>
/// Implementing expressions define GraphQL fields.
/// </summary>
public interface IHaveFields
{
    /// <summary>
    /// Add a field to the expression.
    /// </summary>
    /// <param name="field">The field to add.</param>
    /// <returns>The added field.</returns>
    FieldExpression WithField(FieldExpression field);
}
