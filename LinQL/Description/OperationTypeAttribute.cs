namespace LinQL.Description;

/// <summary>
/// Mark a class as a root type.
/// </summary>
/// <remarks>
/// Set the operation type for the root type.
/// </remarks>
/// <param name="operationType">The root operation type.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class OperationTypeAttribute(RootOperationType operationType = RootOperationType.Query) : Attribute
{

    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public RootOperation Operation { get; } = RootOperation.From(operationType);
}
