namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using LinQL.Description;
using LinQL.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/// <summary>
/// The schema an SDL document describes, together with the rules that name and map what it declares.
/// </summary>
/// <remarks>
/// The walk that fills this in and the emitter that renders it are separate passes, so the naming,
/// type-mapping and filter-input rules live here rather than in the emitter. A diagnostic and a
/// generated declaration then answer "is this a filter input?" the same way instead of each applying
/// its own copy of the rule.
/// </remarks>
internal sealed class SchemaModel
{
    /// <summary>
    /// The suffix that marks a filter input type.
    /// </summary>
    /// <remarks>
    /// Hot Chocolate names the input type of a <c>where</c> argument this way, and the generator keys
    /// its filter builders, extensions and diagnostics on it.
    /// </remarks>
    internal const string FilterInputSuffix = "FilterInput";

    private readonly HashSet<string> inputObjectTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the root operations the document declares.
    /// </summary>
    public List<RootTypeClass> RootOperations { get; } = [];

    /// <summary>
    /// Gets every type the document declares, in document order.
    /// </summary>
    public List<IClassFactory> Types { get; } = [];

    /// <summary>
    /// Gets the field arguments that get an extension method.
    /// </summary>
    public List<FieldExtension> FieldExtensions { get; } = [];

    /// <summary>
    /// Gets every scalar a document can use: the built-in ones first, then the ones the document
    /// extends the schema with, in document order.
    /// </summary>
    public Dictionary<string, Scalar> KnownScalars { get; } = Scalar.NativeScalars.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);

    /// <summary>
    /// Records a scalar the document declared.
    /// </summary>
    /// <param name="scalar">The scalar.</param>
    public void AddScalar(Scalar scalar) => this.KnownScalars.Add(scalar.Name, scalar);

    /// <summary>
    /// Records an input object the document declared.
    /// </summary>
    /// <param name="name">The type name.</param>
    public void AddInputObjectType(string name) => this.inputObjectTypes.Add(name);

    /// <summary>
    /// Gets whether the document declares the input object.
    /// </summary>
    /// <param name="name">The type name.</param>
    /// <returns><see langword="true"/> when an input object with that name was declared.</returns>
    public bool HasInputObjectType(string name) => this.inputObjectTypes.Contains(name);

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
    /// Gets the C# type a field or argument of this GraphQL type is declared as.
    /// </summary>
    /// <param name="type">The GraphQL type.</param>
    /// <returns>The C# type name, with list and nullability suffixes applied.</returns>
    public string TypeName(ITypeNode type)
    {
        var typeName = this.KnownScalars.TryGetValue(type.NamedType().Name.Value, out var mapped)
            ? mapped.RuntimeType : type.NamedType().Name.Value;

        typeName = type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType()) ? typeName + "[]" : typeName;

        return type.IsNonNullType() ? typeName : typeName + "?";
    }

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
