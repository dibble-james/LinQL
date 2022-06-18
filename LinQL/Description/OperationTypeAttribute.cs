namespace LinQL.Description;

/// <summary>
/// Mark a class as a root type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class OperationTypeAttribute : Attribute
{
    /// <summary>
    /// Set the operation type for the root type.
    /// </summary>
    /// <param name="operationType">The root operation type.</param>
    public OperationTypeAttribute(RootOperationType operationType = RootOperationType.Query)
        => this.Operation = RootOperation.From(operationType);

    /// <summary>
    /// Gets or sets the operation type.
    /// </summary>
    public RootOperation Operation { get; }
}
