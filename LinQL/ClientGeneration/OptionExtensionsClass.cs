namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class OptionExtensionsClass : IClassFactory
{
    private readonly List<string> scalars;

    public OptionExtensionsClass(List<string> scalars) => this.scalars = scalars;

    public MemberDeclarationSyntax Create() => ClassDeclaration(Identifier("LinQLOptionExtensions"))
        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
        .AddMembers(
            MethodDeclaration(ParseTypeName(nameof(LinqlOptions)), Identifier("WithKnownScalars"))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .AddParameterListParameters(
                    Parameter(Identifier("options")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(nameof(LinqlOptions))))
                .AddBodyStatements(this.scalars.Where(s => !ComplexTypeClass.NativeScalars.ContainsKey(s)).Select(s => ParseStatement($"options.Scalars.Add(typeof({s}));")).ToArray())
                .AddBodyStatements(ParseStatement("return options;")));
}
