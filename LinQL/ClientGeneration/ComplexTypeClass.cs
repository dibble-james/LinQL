namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class ComplexTypeClass : IClassFactory
{
    private readonly Dictionary<string, string> fields = new();

    public ComplexTypeClass(string name) => this.Name = name;

    public string Name { get; }

    public ComplexTypeClass WithField(string name, string type)
    {
        this.fields.Add(name, type);

        return this;
    }

    public MemberDeclarationSyntax Create()
        => ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.fields.Select(f =>
                PropertyDeclaration(ParseTypeName(f.Value), Identifier(f.Key))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))))
                .ToArray());
}
