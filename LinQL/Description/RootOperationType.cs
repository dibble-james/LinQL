namespace LinQL.Description;

using System;

/// <summary>
/// Root operation types.
/// </summary>
public enum RootOperationType
{
    /// <summary>
    /// A query.
    /// </summary>
    Query,

    /// <summary>
    /// A mutation.
    /// </summary>
    Mutation,

    /// <summary>
    /// A subscription.
    /// </summary>
    Subscription,
}

/// <summary>
/// Root Operation details.
/// </summary>
public class RootOperation
{
    /// <summary>
    /// A query.
    /// </summary>
    public static readonly RootOperation Query = new(RootOperationType.Query, "query");

    /// <summary>
    /// A mutation.
    /// </summary>
    public static readonly RootOperation Mutation = new(RootOperationType.Mutation, "mutation");

    /// <summary>
    /// A subscription.
    /// </summary>
    public static readonly RootOperation Subscription = new(RootOperationType.Subscription, "subscription");

    /// <summary>
    /// Convert from a <see cref="RootOperationType"/>.
    /// </summary>
    /// <param name="operationType">The type to convert.</param>
    /// <returns>The corresponding <see cref="RootOperation"/>.</returns>
    public static RootOperation From(RootOperationType operationType) => operationType switch
    {
        RootOperationType.Mutation => Mutation,
        RootOperationType.Subscription => Subscription,
        RootOperationType.Query => Query,
        _ => throw new NotSupportedException(),
    };

    /// <summary>
    /// Gets the operation type.
    /// </summary>
    public RootOperationType Type { get; }

    /// <summary>
    /// Gets the name for serialising to a query string.
    /// </summary>
    public string Name { get; }

    private RootOperation(RootOperationType type, string name)
        => (this.Type, this.Name) = (type, name);
}
