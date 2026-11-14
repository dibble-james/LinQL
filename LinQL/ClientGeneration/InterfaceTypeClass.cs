namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class InterfaceTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces, LinQLTypesAs typesAs = LinQLTypesAs.Class, bool canHaveIncludes = true) : ComplexTypeClass(name, fields, interfaces, typesAs, canHaveIncludes)
{
    protected override TypeDeclarationSyntax Type => InterfaceDeclaration(Identifier(this.Name))
            .AddAttributeLists([AttributeList(SeparatedList(
            [
                Attribute(IdentifierName(nameof(GraphQLTypeAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{this.Name}""")))))
            ]))]);

    protected override Func<FieldDefinitionNode, IEnumerable<MemberDeclarationSyntax>> CreateOperation(IDictionary<string, Scalar> knownScalars)
        => f =>
        [
            PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(this.MutatorAccessor),
            MethodDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier("Execute" + FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SeparatedList(
                    [
                        Attribute(IdentifierName(nameof(GraphQLOperationAttribute).AttributeName())),
                        Attribute(IdentifierName(nameof(GraphQLFieldAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{f.Name.Value}""")))))
                    ])))
                     .AddParameterListParameters(
                        [.. f.Arguments.Select(p => Parameter(IdentifierFor(p.Name.Value))
                            .WithType(ParseTypeName(TypeName(p.Type, knownScalars)))
                            .AddAttributeLists(AttributeList(SeparatedList(
                            [
                                Attribute(IdentifierName(nameof(GraphQLArgumentAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"GQLType = ""{p.Type}""")))))
                            ]))))])
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
        ];
}
