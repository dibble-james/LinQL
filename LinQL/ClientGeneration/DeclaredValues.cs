namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// A value a declared query writes into its document, as the compiler saw it at the declaration.
/// </summary>
/// <remarks>
/// The tree is read where the semantic model is and consumed where the schema is, because resolving a value
/// needs the first and naming it needs the second. It holds no symbols and no syntax, so two runs over
/// unchanged source compare equal.
/// </remarks>
internal abstract record DeclaredValue;

/// <summary>
/// A value the compiler folded to a constant.
/// </summary>
/// <param name="Constant">The constant: a string, a number, a boolean, a character or <see langword="null"/>.</param>
internal sealed record DeclaredConstant(object? Constant) : DeclaredValue;

/// <summary>
/// An enum member, whose name is the value's name in the document.
/// </summary>
/// <param name="Member">The member's name.</param>
internal sealed record DeclaredEnumMember(string Member) : DeclaredValue;

/// <summary>
/// An object initialiser.
/// </summary>
/// <param name="Fields">The properties it sets, in source order.</param>
internal sealed record DeclaredObject(IReadOnlyList<DeclaredValueField> Fields) : DeclaredValue;

/// <summary>
/// One property an object initialiser sets.
/// </summary>
/// <param name="Name">The property's name, before it is resolved to the input field the schema names.</param>
/// <param name="Value">The value it is set to.</param>
internal sealed record DeclaredValueField(string Name, DeclaredValue Value);

/// <summary>
/// A collection or array initialiser.
/// </summary>
/// <param name="Items">The items, in source order.</param>
internal sealed record DeclaredList(IReadOnlyList<DeclaredValue> Items) : DeclaredValue;

/// <summary>
/// One of the declaration's parameters, which becomes a variable wherever it stands.
/// </summary>
/// <param name="Name">The parameter's name.</param>
internal sealed record DeclaredParameter(string Name) : DeclaredValue;

/// <summary>
/// A value the declaration writes, and where in the lambda it wrote it.
/// </summary>
/// <param name="Start">The expression's start, which finds it again when the declaration is translated.</param>
/// <param name="Value">The value.</param>
internal sealed record DeclaredValueAt(int Start, DeclaredValue Value);

/// <summary>
/// Reads the values a declared query writes as its arguments.
/// </summary>
/// <remarks>
/// Only what the compiler can see through is read: constants and literals, enum members, and object and
/// collection initialisers of the same. Everything else is left out, so the translator reports it instead of
/// writing a value the document does not really carry.
/// </remarks>
internal static class DeclaredValues
{
    /// <summary>
    /// Reads every value the lambda writes as an argument.
    /// </summary>
    /// <param name="lambda">The declaration's lambda.</param>
    /// <param name="semantics">The semantic model of the tree the lambda was parsed from.</param>
    /// <returns>The values, ordered by where they appear.</returns>
    public static IReadOnlyList<DeclaredValueAt> ReadAll(LambdaExpressionSyntax lambda, SemanticModel semantics)
        => [.. lambda.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Select(x => (Start: x.Expression.SpanStart, Value: Read(x.Expression, semantics)))
            .Where(x => x.Value is not null)
            .Select(x => new DeclaredValueAt(x.Start, x.Value!))
            .OrderBy(x => x.Start)];

    /// <summary>
    /// Reads one expression as a value.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="semantics">The semantic model of the expression's tree.</param>
    /// <returns>The value, or <see langword="null"/> when the compiler cannot see through it.</returns>
    public static DeclaredValue? Read(ExpressionSyntax expression, SemanticModel semantics)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return Read(parenthesized.Expression, semantics);
        }

        // An enum member is a constant to the compiler, but its document value is the member's name, so it is
        // read before the constant.
        if (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax
            && semantics.GetSymbolInfo(expression).Symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } member)
        {
            return new DeclaredEnumMember(member.Name);
        }

        if (expression is IdentifierNameSyntax
            && semantics.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter)
        {
            return new DeclaredParameter(parameter.Name);
        }

        if (semantics.GetConstantValue(expression) is { HasValue: true } constant)
        {
            return new DeclaredConstant(constant.Value);
        }

        return expression switch
        {
            BaseObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: > 0 } => null,
            BaseObjectCreationExpressionSyntax { Initializer: { } initializer } => ObjectOrList(initializer, semantics),
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => List(initializer, semantics),
            ImplicitArrayCreationExpressionSyntax array => List(array.Initializer, semantics),
            _ => null,
        };
    }

    /// <summary>
    /// Reads an initialiser as an object or as a list, whichever it is.
    /// </summary>
    /// <param name="initializer">The initialiser.</param>
    /// <param name="semantics">The semantic model of the initialiser's tree.</param>
    /// <returns>The value, or <see langword="null"/> when any part of it cannot be read.</returns>
    private static DeclaredValue? ObjectOrList(InitializerExpressionSyntax initializer, SemanticModel semantics)
    {
        if (initializer.Kind() == SyntaxKind.CollectionInitializerExpression)
        {
            return List(initializer, semantics);
        }

        if (initializer.Kind() != SyntaxKind.ObjectInitializerExpression)
        {
            return null;
        }

        List<DeclaredValueField> fields = [];

        foreach (var assignment in initializer.Expressions)
        {
            if (assignment is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } set
                || Read(set.Right, semantics) is not { } value)
            {
                return null;
            }

            fields.Add(new DeclaredValueField(name.Identifier.ValueText, value));
        }

        return new DeclaredObject(fields);
    }

    private static DeclaredValue? List(InitializerExpressionSyntax initializer, SemanticModel semantics)
    {
        List<DeclaredValue> items = [];

        foreach (var item in initializer.Expressions)
        {
            if (Read(item, semantics) is not { } value)
            {
                return null;
            }

            items.Add(value);
        }

        return new DeclaredList(items);
    }
}
