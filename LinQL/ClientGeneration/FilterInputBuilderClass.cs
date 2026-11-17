namespace LinQL.ClientGeneration;

using System.Linq;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

// Generates the fluent builder for a filter input type, so filters can be written as f => f.Price(p => p.Gt(10))
// instead of object initialisers.
internal class FilterInputBuilderClass(string name) : IClassFactory
{
    public MemberDeclarationSyntax Create(SchemaModel model)
        => ClassDeclaration(Identifier(SchemaNames.FilterBuilderName(name)))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers([.. model.FieldsOf(name).Select(x => CreateField(name, x, model))]);

    private static MethodDeclarationSyntax CreateField(string builder, FieldDefinitionNode field, SchemaModel model)
    {
        var nestedType = field.Type.NamedType().Name.Value;
        var isNested = SchemaNames.IsFilterInputType(nestedType);
        var isList = isNested && (field.Type.IsListType() || (field.Type.IsNonNullType() && field.Type.InnerType().IsListType()));

        var parameterType = isNested
            ? $"System.Func<{SchemaNames.FilterBuilderName(nestedType)}, {SchemaNames.FilterBuilderName(nestedType)}>"
            : model.TypeName(field.Type);

        var name = AttributeArgument(ParseExpression($@"Name = ""{field.Name.Value}"""));
        var attribute = isList
            ? Attribute(
                IdentifierName(nameof(GraphQLFilterFieldAttribute).AttributeName()),
                AttributeArgumentList(SeparatedList([name, AttributeArgument(ParseExpression("IsList = true"))])))
            : Attribute(
                IdentifierName(nameof(GraphQLFilterFieldAttribute).AttributeName()),
                AttributeArgumentList(SingletonSeparatedList(name)));

        return MethodDeclaration(ParseTypeName(SchemaNames.FilterBuilderName(builder)), Identifier(SchemaNames.FieldName(field.Name.Value)))
            .AddModifiers(Token(SyntaxKind.PublicKeyword))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(attribute)))
            .AddParameterListParameters(
                Parameter(SchemaNames.IdentifierFor(field.Name.Value))
                    .WithType(ParseTypeName(parameterType)))
            .WithExpressionBody(ArrowExpressionClause(ThisExpression()))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
}
