namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class InterfaceTypeClass(string name, IEnumerable<string> interfaces, LinQLTypesAs typesAs = LinQLTypesAs.Class, bool canHaveIncludes = true) : ComplexTypeClass(name, interfaces, typesAs, canHaveIncludes)
{
    protected override TypeDeclarationSyntax Type => InterfaceDeclaration(Identifier(this.Name))
            .AddAttributeLists([AttributeList(SeparatedList(
            [
                Attribute(IdentifierName(nameof(GraphQLTypeAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{this.Name}""")))))
            ]))]);

    protected override bool ImplementsCommands => false;
}
