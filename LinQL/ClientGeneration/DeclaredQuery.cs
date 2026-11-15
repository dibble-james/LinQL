namespace LinQL.ClientGeneration;

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// A <c>[LinQLQuery]</c> declaration the generator found, before it is bound to a schema.
/// </summary>
/// <param name="ClientNamespace">The client namespace the declaration names.</param>
/// <param name="FieldName">The declared field's name.</param>
/// <param name="RootType">The root type of the query's expression, or <see langword="null"/> when the field is not one.</param>
/// <param name="IsStatic">Whether the field is static, which a declared query has to be.</param>
/// <param name="Body">The lambda the field holds, which the translator reads.</param>
/// <param name="Location">The declaration.</param>
internal sealed record DeclaredQuery(string ClientNamespace, string FieldName, string? RootType, bool IsStatic, LambdaExpressionSyntax? Body, Location Location)
{
    /// <summary>
    /// Reads a declaration from the attribute application marking its field.
    /// </summary>
    /// <param name="attribute">The attribute application, whose target is the marked field.</param>
    /// <returns>The declaration, or <see langword="null"/> when the attribute is not on a field.</returns>
    public static DeclaredQuery? Find(GeneratorAttributeSyntaxContext attribute)
    {
        if (attribute.TargetSymbol is not IFieldSymbol field)
        {
            return null;
        }

        var clientNamespace = attribute.Attributes[0].ConstructorArguments[0].Value as string ?? string.Empty;

        return new DeclaredQuery(
            clientNamespace,
            field.Name,
            RootTypeOf(field.Type),
            field.IsStatic,
            (attribute.TargetNode as VariableDeclaratorSyntax)?.Initializer?.Value as LambdaExpressionSyntax,
            field.Locations.FirstOrDefault() ?? Location.None);
    }

    /// <summary>
    /// Reads the root type from an <c>Expression&lt;Func&lt;TRoot, TData&gt;&gt;</c> field.
    /// </summary>
    /// <param name="type">The field's declared type.</param>
    /// <returns>The root type's name, or <see langword="null"/> when the type is not such an expression.</returns>
    private static string? RootTypeOf(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { Name: "Expression", Arity: 1 } expression
            || expression.ContainingNamespace.ToDisplayString() != "System.Linq.Expressions")
        {
            return null;
        }

        if (expression.TypeArguments[0] is not INamedTypeSymbol delegateType
            || delegateType.Name != "Func"
            || delegateType.TypeArguments.Length < 2
            || delegateType.ContainingNamespace.ToDisplayString() != "System")
        {
            return null;
        }

        return delegateType.TypeArguments[0].Name;
    }
}
