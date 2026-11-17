namespace LinQL;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LinQL.Caching;
using LinQL.Description;
using LinQL.Filters;

/// <summary>
/// A declared query: the document generated at build time and the projection that turns a response into
/// <typeparamref name="TData"/>.
/// </summary>
/// <typeparam name="TRoot">The root operation type the document queries.</typeparam>
/// <typeparam name="TData">The projected result type.</typeparam>
/// <param name="document">The document the generator emitted from the declaration.</param>
/// <param name="operationType">The operation the document is.</param>
/// <param name="projection">The declaration's expression, which a response is projected through.</param>
/// <param name="variables">The value of each variable the document declares, by the name it uses.</param>
/// <remarks>
/// The document does not depend on the values, so it is generated once; the values are bound to the variables the
/// document declares, and the projection is the declaration's own expression, which the adapter running the query
/// compiles. A generated binder returns this, so the call site is
/// <c>client.SendAsync(Queries.FilmById("1"))</c>.
/// </remarks>
public sealed class LinQLQuery<TRoot, TData>(
    string document,
    RootOperationType operationType,
    Expression<Func<TRoot, TData>> projection,
    IReadOnlyDictionary<string, object?> variables)
    where TRoot : RootType<TRoot>
{
    /// <summary>
    /// Gets the document to send, which does not depend on the values.
    /// </summary>
    public string Document { get; } = document;

    /// <summary>
    /// Gets the operation the document is.
    /// </summary>
    public RootOperationType OperationType { get; } = operationType;

    /// <summary>
    /// Gets the declaration's expression, which a response is projected through.
    /// </summary>
    public Expression<Func<TRoot, TData>> Projection { get; } = projection;

    /// <summary>
    /// Gets the value of each variable the document declares, by the name it uses.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Variables { get; } = variables;

    /// <summary>
    /// Gets the entity-cache hook the generator emitted for this declaration, or <see langword="null"/> when the
    /// operation does not take part in entity caching.
    /// </summary>
    public LinQLOperationCache<TRoot>? Cache { get; private init; }

    /// <summary>
    /// Attaches the entity-cache hook the generator emitted for this declaration.
    /// </summary>
    /// <param name="cache">The hook.</param>
    /// <returns>The query, with its cache hook attached.</returns>
    /// <remarks>
    /// The generated binder calls this, so a hand-written query keeps the operation-level cache it always had.
    /// </remarks>
    public LinQLQuery<TRoot, TData> WithCache(LinQLOperationCache<TRoot> cache)
        => new(this.Document, this.OperationType, this.Projection, this.Variables) { Cache = cache };

    /// <summary>
    /// Binds values to the variables a generated document declares.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="operationType">The operation the document is.</param>
    /// <param name="projection">The declaration's expression, which carries the query's parameters.</param>
    /// <param name="values">The value of each variable, by the name the document uses.</param>
    /// <returns>The query.</returns>
    /// <remarks>
    /// Values are normalised the way the runtime translator normalises an attached argument's value, so an input
    /// object reaches the wire in camelCase with enums by name and a filter as the dictionaries Hot Chocolate
    /// expects, rather than in CLR shape.
    /// The declaration's parameters are document values, not projection inputs, so they are replaced with their
    /// defaults to leave the single-parameter projection a response is read through.
    /// </remarks>
    public static LinQLQuery<TRoot, TData> Bind(
        string document,
        RootOperationType operationType,
        LambdaExpression projection,
        params (string Variable, object? Value)[] values)
        => new(document, operationType, Project(projection), values.ToDictionary(x => x.Variable, x => FilterValue.Normalize(x.Value), StringComparer.Ordinal));

    /// <summary>
    /// Binds one value to the variable a generated document declares.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="operationType">The operation the document is.</param>
    /// <param name="projection">The declaration's expression, which carries the query's parameters.</param>
    /// <param name="value">The variable's name and its value.</param>
    /// <returns>The query.</returns>
    /// <remarks>
    /// A single tuple argument would otherwise be read as the whole array the parameter takes, so a generated
    /// binder with one value needs this overload to be written the same way as one with several.
    /// </remarks>
    public static LinQLQuery<TRoot, TData> Bind(
        string document,
        RootOperationType operationType,
        LambdaExpression projection,
        (string Variable, object? Value) value)
        => Bind(document, operationType, projection, [value]);

    // Reduces a declared query's expression to the projection a response is read through.
    //
    // A declaration's parameters are document values, not projection inputs, so replacing them with their
    // defaults leaves the body that depends on the response alone. The generator reports a projection that
    // reads one as LINQLGEN10, so a body reaching this method does not depend on a value.
    //
    // Returns the projection, which takes only the root.
    private static Expression<Func<TRoot, TData>> Project(LambdaExpression declaration)
    {
        var values = new HashSet<ParameterExpression>(declaration.Parameters.Skip(1));

        return Expression.Lambda<Func<TRoot, TData>>(
            new DefaultValues(values).Visit(declaration.Body),
            declaration.Parameters[0]);
    }

    // Replaces a declaration's value parameters with their defaults.
    private sealed class DefaultValues(HashSet<ParameterExpression> parameters) : ExpressionVisitor
    {
        /// <inheritdoc />
        protected override Expression VisitParameter(ParameterExpression node)
            => parameters.Contains(node) ? Expression.Default(node.Type) : base.VisitParameter(node);
    }
}
