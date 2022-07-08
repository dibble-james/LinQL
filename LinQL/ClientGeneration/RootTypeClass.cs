namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class RootTypeClass : IClassFactory
{
    public RootTypeClass(string name) => this.Name = name;

    public string Name { get; }

    public MemberDeclarationSyntax Create()
    {
        var rootType = ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddBaseListTypes(SimpleBaseType(IdentifierName($"{nameof(RootType<object>)}<{this.Name}>")));

        return rootType;
    }
}
