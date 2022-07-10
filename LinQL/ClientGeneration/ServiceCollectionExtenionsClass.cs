namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ServiceCollectionExtenionsClass : IClassFactory
{
    private readonly string graphName;

    public ServiceCollectionExtenionsClass(string graphName) => this.graphName = graphName;

    public MemberDeclarationSyntax Create()
        => ClassDeclaration(Identifier("ServiceCollectionExtensions"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(MethodDeclaration(
                IdentifierName($"GraphBuilder<{this.graphName}>"),
                Identifier($"Add{this.graphName}"))
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
                .AddParameterListParameters(
                    Parameter(Identifier("services")).WithType(IdentifierName("this IServiceCollection")))
                .WithExpressionBody(ArrowExpressionClause(ParseExpression($"services.AddGraphQLClient<{this.graphName}>()")))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                .WithTrailingTrivia(Whitespace("\n"))
            );
}
