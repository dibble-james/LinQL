namespace LinQL.ClientGeneration;

using LinQL.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

/// <summary>
/// The rules that turn GraphQL names into the C# names the generator emits.
/// </summary>
/// <remarks>
/// Every rule here is a pure function of a name, so they live apart from <see cref="SchemaModel"/>:
/// an emitter or a diagnostic can apply them without holding a schema.
/// </remarks>
internal static class SchemaNames
{
    /// <summary>
    /// The suffix that marks a filter input type.
    /// </summary>
    /// <remarks>
    /// Hot Chocolate names the input type of a <c>where</c> argument this way, and the generator keys
    /// its filter builders, extensions and diagnostics on it.
    /// </remarks>
    internal const string FilterInputSuffix = "FilterInput";

    /// <summary>
    /// Gets whether a type name is a filter input type's name.
    /// </summary>
    /// <param name="typeName">The type name.</param>
    /// <returns><see langword="true"/> when the name ends in the filter input suffix.</returns>
    public static bool IsFilterInputType(string typeName)
        => typeName.Length > FilterInputSuffix.Length && typeName.EndsWith(FilterInputSuffix, StringComparison.Ordinal);

    /// <summary>
    /// Gets the name of the fluent builder generated for a filter input type.
    /// </summary>
    /// <param name="filterTypeName">The filter input type's name.</param>
    /// <returns>The builder's name.</returns>
    public static string FilterBuilderName(string filterTypeName) => filterTypeName + "Builder";

    /// <summary>
    /// Gets the C# member name for a GraphQL field or argument name.
    /// </summary>
    /// <param name="field">The GraphQL name.</param>
    /// <returns>The name with its first character upper-cased.</returns>
    public static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);

    /// <summary>
    /// Gets the identifier for a GraphQL name, escaping it when it is a C# keyword.
    /// </summary>
    /// <param name="name">The GraphQL name.</param>
    /// <returns>The identifier.</returns>
    public static SyntaxToken IdentifierFor(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            ? SyntaxFactory.VerbatimIdentifier(SyntaxTriviaList.Empty, name, name, SyntaxTriviaList.Empty)
            : SyntaxFactory.Identifier(name);
}
