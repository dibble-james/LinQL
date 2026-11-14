namespace LinQL.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Linq;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/// <summary>
/// Generates the <c>Includes</c> extension methods that add fields to a selection.
/// </summary>
/// <remarks>
/// One method is emitted per generated selection type, plus an element overload for collections. A
/// single generic method (<c>Includes&lt;T&gt;(this T, …)</c>) is not used: it would offer itself on
/// <c>string</c> and <c>int</c>, where adding a selection has no meaning. Both overloads take the
/// element type in the lambda, so a collection include reads <c>q.Products.Includes(p => p.Tags)</c>.
/// </remarks>
internal class IncludesExtensionsClass(IEnumerable<string> types) : IClassFactory
{
    public MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
        => ClassDeclaration(Identifier("IncludesExtensions"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers([.. types
                .Distinct(StringComparer.Ordinal)
                .SelectMany(CreateMembers)]);

    private static IEnumerable<MemberDeclarationSyntax> CreateMembers(string type)
    {
        yield return CreateMember(type, type);
        yield return CreateMember(type + "[]", type);
    }

    private static MethodDeclarationSyntax CreateMember(string receiver, string element)
        => MethodDeclaration(ParseTypeName(receiver), Identifier("Includes"))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(IdentifierName(nameof(GraphQLIncludesAttribute).AttributeName())))))
            .AddParameterListParameters(
                Parameter(Identifier("source")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(receiver)),
                Parameter(Identifier("include")).WithType(ParseTypeName($"System.Linq.Expressions.Expression<System.Func<{element}, object>>")))
            .WithExpressionBody(ArrowExpressionClause(IdentifierName("source")))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
}
