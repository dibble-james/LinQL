namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/// <summary>
/// Generates the fluent builder for a filter input type, so filters can be written as
/// <c>f => f.Price(p => p.Gt(10))</c> instead of object initialisers.
/// </summary>
internal class FilterInputBuilderClass(string name, IReadOnlyList<FieldDefinitionNode> fields) : IClassFactory
{
    public MemberDeclarationSyntax Create(SchemaModel model)
        => ClassDeclaration(Identifier(SchemaModel.FilterBuilderName(name)))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers([.. fields.Select(x => CreateField(name, x, model))]);

    private static MethodDeclarationSyntax CreateField(string builder, FieldDefinitionNode field, SchemaModel model)
    {
        var nestedType = field.Type.NamedType().Name.Value;
        var isNested = SchemaModel.IsFilterInputType(nestedType);
        var isList = isNested && (field.Type.IsListType() || (field.Type.IsNonNullType() && field.Type.InnerType().IsListType()));

        var parameterType = isNested
            ? $"System.Func<{SchemaModel.FilterBuilderName(nestedType)}, {SchemaModel.FilterBuilderName(nestedType)}>"
            : model.TypeName(field.Type);

        var name = AttributeArgument(ParseExpression($@"Name = ""{field.Name.Value}"""));
        var attribute = isList
            ? Attribute(
                IdentifierName(nameof(GraphQLFilterFieldAttribute).AttributeName()),
                AttributeArgumentList(SeparatedList<AttributeArgumentSyntax>([name, AttributeArgument(ParseExpression("IsList = true"))])))
            : Attribute(
                IdentifierName(nameof(GraphQLFilterFieldAttribute).AttributeName()),
                AttributeArgumentList(SingletonSeparatedList(name)));

        return MethodDeclaration(ParseTypeName(SchemaModel.FilterBuilderName(builder)), Identifier(SchemaModel.FieldName(field.Name.Value)))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(attribute)))
            .AddParameterListParameters(
                Parameter(SchemaModel.IdentifierFor(field.Name.Value))
                    .WithType(ParseTypeName(parameterType)))
            .WithExpressionBody(ArrowExpressionClause(ThisExpression()))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
}
