namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ComplexTypeClass : IClassFactory
{
    private readonly IReadOnlyList<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name, IReadOnlyList<FieldDefinitionNode> fields)
        => (this.Name, this.fields) = (name, fields);

    public string Name { get; }

    public MemberDeclarationSyntax Create()
        => ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.Properties.Select(f =>
                PropertyDeclaration(ParseTypeName(f.Type.NamedType().Name.Value), Identifier(f.Name.Value))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))))
                .ToArray())
            .AddMembers(this.Methods.Select(f =>
                MethodDeclaration(ParseTypeName(f.Type.NamedType().Name.Value), Identifier(f.Name.Value))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddParameterListParameters(
                        f.Arguments.Select(p => Parameter(Identifier(p.Name.Value)).WithType(ParseTypeName(p.Type.NamedType().Name.Value))).ToArray())
                    .WithBody(Block(ParseStatement("return default!;"))))
                .ToArray());

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());
}
