namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class InterfaceTypeClass : ComplexTypeClass
{
    public InterfaceTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces)
        : base(name, fields, interfaces)
    {
    }

    protected override TypeDeclarationSyntax Type => InterfaceDeclaration(Identifier(this.Name));
}
