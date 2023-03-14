namespace LinQL.Expressions;

/// <summary>
/// A variable definition
/// </summary>
/// <param name="Name">The name of the variable</param>
/// <param name="Type">The graphql type of the variable</param>
/// <param name="Value">The value of the variable</param>
public record struct Variable(string Name, string Type, object? Value);
