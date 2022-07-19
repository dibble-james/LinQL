namespace LinQL.ClientGeneration;

using LinQL.Translation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class GraphClass : IClassFactory
{

    public GraphClass(string name) => this.Name = name;

    public string Name { get; }

    public List<RootTypeClass> RootOperations { get; } = new();

    public List<IClassFactory> Types { get; } = new();

    public MemberDeclarationSyntax Create()
    {
        var constructor = ConstructorDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(
                Parameter(Identifier("logger")).WithType(ParseTypeName($"ILogger<{this.Name}>")),
                Parameter(Identifier("options")).WithType(ParseTypeName($"{nameof(GraphOptions)}<{this.Name}>")),
                Parameter(Identifier("queryTranslator")).WithType(ParseTypeName(nameof(IQueryTranslator))))
            .WithInitializer(
                ConstructorInitializer(SyntaxKind.BaseConstructorInitializer)
                .AddArgumentListArguments(
                    Argument(IdentifierName("logger")),
                    Argument(IdentifierName("options")),
                    Argument(IdentifierName("queryTranslator"))))
            .WithBody(Block());

        var graph = ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName(nameof(Graph))))
            .AddMembers(constructor)
            .AddMembers(this.RootOperations.Select(root =>
                PropertyDeclaration(ParseTypeName(root.Name), root.Name)
                    .AddModifiers(Token(SyntaxKind.PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression($"this.RootType<{root.Name}>()")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
                    .WithTrailingTrivia(Whitespace("\n")))
                .ToArray());

        return graph;
    }
}
