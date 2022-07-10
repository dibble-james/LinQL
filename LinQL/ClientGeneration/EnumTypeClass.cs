namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class EnumTypeClass : IClassFactory
{
    private readonly string name;
    private readonly IEnumerable<EnumValueDefinitionNode> values;

    public EnumTypeClass(string name, IEnumerable<EnumValueDefinitionNode> values)
        => (this.name, this.values) = (name, values);

    public MemberDeclarationSyntax Create()
        => EnumDeclaration(Identifier(this.name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .WithMembers(SeparatedList(this.values.Select(x => EnumMemberDeclaration(Identifier(x.Name.Value))).ToArray()));
}
