namespace LinQL.ClientGeneration;

using LinQL.Translation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class GraphClass : IClassFactory
{
    private readonly string name;

    public GraphClass(string name) => this.name = name;

    public List<RootTypeClass> RootOperations { get; } = new();

    public List<IClassFactory> Types { get; } = new();

    public MemberDeclarationSyntax Create()
    {
        var constructor = ConstructorDeclaration(Identifier(this.name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddParameterListParameters(
                Parameter(Identifier("logger")).WithType(ParseTypeName($"ILogger<{this.name}>")),
                Parameter(Identifier("connection")).WithType(ParseTypeName(nameof(IGraphQLConnection))),
                Parameter(Identifier("queryTranslator")).WithType(ParseTypeName(nameof(IQueryTranslator))))
            .WithInitializer(
                ConstructorInitializer(SyntaxKind.BaseConstructorInitializer)
                .AddArgumentListArguments(
                    Argument(IdentifierName("logger")),
                    Argument(IdentifierName("connection")),
                    Argument(IdentifierName("queryTranslator"))))
            .WithBody(Block());

        var graph = ClassDeclaration(Identifier(this.name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName(nameof(Graph))))
            .AddMembers(constructor)
            .AddMembers(this.RootOperations.Select(root =>
                PropertyDeclaration(ParseTypeName(root.Name), root.Name)
                    .AddModifiers(Token(SyntaxKind.PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression($"this.RootType<{root.Name}>();"))))
                .ToArray());

        return graph;
    }
}
