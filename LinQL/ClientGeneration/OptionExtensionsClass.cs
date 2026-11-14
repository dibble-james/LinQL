namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class OptionExtensionsClass : IClassFactory
{
    public MemberDeclarationSyntax Create(SchemaModel model) => ClassDeclaration(Identifier("LinQLOptionExtensions"))
        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
        .AddMembers(
            MethodDeclaration(ParseTypeName(nameof(LinQLOptions)), Identifier("WithKnownScalars"))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .AddParameterListParameters(
                    Parameter(Identifier("options")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(nameof(LinQLOptions))))
                .AddBodyStatements([.. model.KnownScalars.Values.Select(s => ParseStatement(@$"options.Scalars.Add(new(""{s.Name}"", ""{s.RuntimeType}""));"))])
                .AddBodyStatements(ParseStatement("return options;")));
}
