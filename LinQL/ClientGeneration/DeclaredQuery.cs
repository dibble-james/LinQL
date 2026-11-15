namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// A <c>[LinQLQuery]</c> declaration the generator found, before it is bound to a schema.
/// </summary>
/// <param name="ClientNamespace">The client namespace the declaration names.</param>
/// <param name="FieldName">The declared field's name.</param>
/// <param name="ContainingType">The name of the type declaring the field, which the emitted name is built from.</param>
/// <param name="Namespace">The namespace the declaring type lives in, or <see langword="null"/> for the global namespace.</param>
/// <param name="RootType">The root type of the query's expression, or <see langword="null"/> when the field is not one.</param>
/// <param name="Parameters">The lambda parameters after the root, which are the values the query sends.</param>
/// <param name="IsStatic">Whether the field is static, which a declared query has to be.</param>
/// <param name="Body">The lambda the field holds, which the translator reads.</param>
/// <param name="Values">The values its arguments write, read while the semantic model is available.</param>
/// <param name="Location">The declaration.</param>
internal sealed record DeclaredQuery(
    string ClientNamespace,
    string FieldName,
    string ContainingType,
    string? Namespace,
    string? RootType,
    IReadOnlyList<DeclaredQueryParameter> Parameters,
    bool IsStatic,
    LambdaExpressionSyntax? Body,
    IReadOnlyList<DeclaredValueAt> Values,
    Location Location)
{
    /// <summary>
    /// The format the binder's parameter types are written in.
    /// </summary>
    /// <remarks>
    /// Fully qualified, because the generated file has none of the declaring file's usings, and with nullable
    /// annotations kept, because the parameter's nullability is part of what a caller is allowed to pass.
    /// </remarks>
    private static readonly SymbolDisplayFormat ValueTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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

        var body = (attribute.TargetNode as VariableDeclaratorSyntax)?.Initializer?.Value as LambdaExpressionSyntax;

        var signature = SignatureOf(field.Type, body);

        return new DeclaredQuery(
            clientNamespace,
            field.Name,
            field.ContainingType.Name,
            field.ContainingNamespace.IsGlobalNamespace ? null : field.ContainingNamespace.ToDisplayString(),
            signature?.Root,
            signature?.Parameters ?? [],
            field.IsStatic,
            body,
            body is null ? [] : DeclaredValues.ReadAll(body, attribute.SemanticModel),
            field.Locations.FirstOrDefault() ?? Location.None);
    }

    /// <summary>
    /// Reads the root type and the value parameters from an <c>Expression&lt;Func&lt;TRoot, …&gt;&gt;</c> field.
    /// </summary>
    /// <param name="type">The field's declared type.</param>
    /// <param name="body">The lambda the field holds, whose parameter names the binder keeps.</param>
    /// <returns>The signature, or <see langword="null"/> when the type is not such an expression.</returns>
    private static Signature? SignatureOf(ITypeSymbol type, LambdaExpressionSyntax? body)
    {
        if (type is not INamedTypeSymbol { Name: "Expression", Arity: 1 } expression
            || expression.ContainingNamespace.ToDisplayString() != "System.Linq.Expressions"
            || expression.TypeArguments[0] is not INamedTypeSymbol delegateType
            || delegateType.Name != "Func"
            || delegateType.TypeArguments.Length < 2
            || delegateType.ContainingNamespace.ToDisplayString() != "System")
        {
            return null;
        }

        IReadOnlyList<string> names = body switch
        {
            SimpleLambdaExpressionSyntax => [],
            ParenthesizedLambdaExpressionSyntax parenthesized => [.. parenthesized.ParameterList.Parameters.Skip(1).Select(x => x.Identifier.ValueText)],
            _ => [],
        };

        // The values are the delegate's type arguments between the root and the result, and the binder names
        // them the way the lambda did. A lambda whose arity disagrees with the delegate cannot compile, so it
        // takes no parameters rather than one that would not resolve.
        IReadOnlyList<ITypeSymbol> values = [.. delegateType.TypeArguments.Skip(1).Take(delegateType.TypeArguments.Length - 2)];

        return new Signature(
            delegateType.TypeArguments[0].Name,
            names.Count == values.Count
                ? [.. values.Select((x, i) => new DeclaredQueryParameter(names[i], x.ToDisplayString(ValueTypeFormat)))]
                : []);
    }

    /// <summary>
    /// The shape of a declared query's expression.
    /// </summary>
    /// <param name="Root">The root type's name.</param>
    /// <param name="Parameters">The parameters the binder takes, in declaration order.</param>
    private sealed record Signature(string Root, IReadOnlyList<DeclaredQueryParameter> Parameters);
}

/// <summary>
/// One of a declared query's lambda parameters after the root, which the binder takes.
/// </summary>
/// <param name="Name">The parameter's name, as the lambda declares it.</param>
/// <param name="Type">The parameter's C# type, as the field's expression declares it.</param>
internal sealed record DeclaredQueryParameter(string Name, string Type);
