namespace LinQL.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/// <summary>
/// A field argument that gets an extension method.
/// </summary>
/// <param name="Field">The field the argument belongs to.</param>
/// <param name="Argument">The argument itself.</param>
/// <param name="IsFilter">Whether the argument carries a filter, which the translator builds from a lambda.</param>
internal sealed record FieldExtension(FieldDefinitionNode Field, InputValueDefinitionNode Argument, bool IsFilter);

/// <summary>
/// Generates the extension methods that attach field arguments, so a filter is written
/// <c>q.Products.Filter(f => f.Name(n => n.Contains("widget")))</c> rather than passed to a companion.
/// </summary>
/// <remarks>
/// An extension sees only the type it extends, so one method is emitted per receiver type, method name
/// and parameter type. Filter methods are all called <c>Filter</c> — they overload by filter input type —
/// and the other arguments get a method named after them (<c>In</c>, <c>First</c>, <c>Order</c>).
/// </remarks>
internal class FieldExtensionsClass(IEnumerable<FieldExtension> extensions) : IClassFactory
{
    public MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
        => ClassDeclaration(Identifier("FieldExtensions"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers([.. extensions
                .Where(x => !knownScalars.ContainsKey(x.Field.Type.NamedType().Name.Value))
                .SelectMany(x => MethodNames(x).Select(method => new
                {
                    Extension = x,
                    Receiver = ComplexTypeClass.TypeName(x.Field.Type, knownScalars),
                    Method = method,
                    Parameter = ParameterType(x, knownScalars),
                }))
                .GroupBy(x => $"{x.Receiver}|{x.Method}|{x.Parameter}", StringComparer.Ordinal)
                .Select(g => CreateMember(g.First().Extension, g.First().Method, g.First().Receiver, knownScalars))]);

    /// <summary>
    /// Gets the method names an argument is attached by.
    /// </summary>
    /// <remarks>
    /// The schema's own name always comes first. <c>order</c> also gets <c>Sort</c>, because ordering is
    /// the one argument every call site reads as a verb (`q.Products.Sort(...)`), while the rest keep the
    /// schema spelling.
    /// </remarks>
    private static IEnumerable<string> MethodNames(FieldExtension extension)
    {
        if (extension.IsFilter)
        {
            return ["Filter"];
        }

        var name = ComplexTypeClass.FieldName(extension.Argument.Name.Value);

        return name == "Order" ? [name, "Sort"] : [name];
    }

    private static string ParameterType(FieldExtension extension, IDictionary<string, Scalar> knownScalars)
        => extension.IsFilter
            ? BuilderLambda(extension.Argument.Type.NamedType().Name.Value)
            : ComplexTypeClass.TypeName(extension.Argument.Type, knownScalars);

    private static string BuilderLambda(string filterType)
        => $"System.Linq.Expressions.Expression<System.Func<{ComplexTypeClass.FilterBuilderName(filterType)}, {ComplexTypeClass.FilterBuilderName(filterType)}>>";

    private static MethodDeclarationSyntax CreateMember(FieldExtension extension, string method, string receiver, IDictionary<string, Scalar> knownScalars)
    {
        var argument = extension.Argument;
        var parameterType = ParameterType(extension, knownScalars);

        var attribute = extension.IsFilter
            ? Attribute(
                IdentifierName(nameof(GraphQLFilterAttribute).AttributeName()),
                AttributeArgumentList(SeparatedList(
                [
                    AttributeArgument(ParseExpression($@"Argument = ""{argument.Name.Value}""")),
                    AttributeArgument(ParseExpression($@"GQLType = ""{argument.Type}""")),
                ])))
            : Attribute(
                IdentifierName(nameof(GraphQLArgumentAttribute).AttributeName()),
                AttributeArgumentList(SeparatedList(
                [
                    AttributeArgument(ParseExpression($@"Name = ""{argument.Name.Value}""")),
                    AttributeArgument(ParseExpression($@"GQLType = ""{argument.Type}""")),
                ])));

        var parameter = Parameter(extension.IsFilter ? Identifier("filter") : ComplexTypeClass.IdentifierFor(argument.Name.Value))
            .WithType(ParseTypeName(parameterType));

        // An optional argument defaults to null, the same way a filter operation that was never set does
        // not reach the document. The filter itself is never optional: an empty one has no meaning.
        if (!extension.IsFilter && !argument.Type.IsNonNullType())
        {
            parameter = parameter.WithDefault(EqualsValueClause(ParseExpression("null")));
        }

        return MethodDeclaration(ParseTypeName(receiver), Identifier(method))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(attribute)))
            .AddParameterListParameters(
                Parameter(Identifier("source")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(receiver)),
                parameter)
            .WithExpressionBody(ArrowExpressionClause(IdentifierName("source")))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
}
