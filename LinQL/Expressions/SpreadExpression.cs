namespace LinQL.Expressions;

/// <summary>
/// An expression to pick fields from union or implementation types
/// </summary>
/// <remarks>
/// A spread is written as an inline fragment on the type it spreads, so its <see cref="FieldExpression.FieldName"/>
/// is that type's GraphQL name. Two spreads of the same type share a name, and therefore merge into one fragment.
/// </remarks>
public class SpreadExpression : TypeFieldExpression
{
    /// <summary>
    /// Create a new <see cref="SpreadExpression"/>.
    /// </summary>
    /// <param name="typeCondition">The type the fragment is written on.</param>
    /// <param name="root">The root expression.</param>
    public SpreadExpression(Type typeCondition, IRootExpression root)
        : base(typeCondition.GetTypeName(), typeCondition, typeCondition, root)
        => this.TypeCondition = typeCondition;

    private SpreadExpression(Type type, Type typeCondition, IRootExpression root)
        : base(typeCondition.GetTypeName(), type, typeCondition, root)
        => this.TypeCondition = typeCondition;

    /// <summary>
    /// Gets the type the fragment is written on.
    /// </summary>
    /// <remarks>
    /// A selection over a spread produces a value of its own type, so <see cref="FieldExpression.Type"/> changes
    /// with the selection while the type condition stays the type the fragment is written on.
    /// </remarks>
    public Type TypeCondition { get; }

    /// <summary>
    /// Creates the spread that replaces this one when a selection changes its result type.
    /// </summary>
    /// <param name="type">The type of the value the selection produces.</param>
    /// <returns>The replacement.</returns>
    protected override TypeFieldExpression CreateReplacement(Type type)
        => new SpreadExpression(type, this.TypeCondition, this.Root);
}
