namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// A [LinQLQuery] declaration the generator found, before it is bound to a schema.
internal sealed record DeclaredQuery(
    string ClientNamespace,
    string FieldName,
    string ContainingType,
    string? Namespace,
    string? RootType,
    string? ResultType,
    IReadOnlyList<DeclaredQueryParameter> Parameters,
    bool IsStatic,
    LambdaExpressionSyntax? Body,
    IReadOnlyList<DeclaredValueAt> Values,
    Location Location)
{
    // The format the binder's parameter types are written in.
    //
    // Fully qualified, because the generated file has none of the declaring file's usings, and with nullable
    // annotations kept, because the parameter's nullability is part of what a caller is allowed to pass.
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
            signature?.Result,
            signature?.Parameters ?? [],
            field.IsStatic,
            body,
            body is null ? [] : DeclaredValues.ReadAll(body, attribute.SemanticModel),
            field.Locations.FirstOrDefault() ?? Location.None);
    }

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
            delegateType.TypeArguments[delegateType.TypeArguments.Length - 1].ToDisplayString(ValueTypeFormat),
            names.Count == values.Count
                ? [.. values.Select((x, i) => new DeclaredQueryParameter(names[i], x.ToDisplayString(ValueTypeFormat)))]
                : []);
    }

    private sealed record Signature(string Root, string Result, IReadOnlyList<DeclaredQueryParameter> Parameters);
}

// One of a declared query's lambda parameters after the root, which the binder takes.
internal sealed record DeclaredQueryParameter(string Name, string Type);
