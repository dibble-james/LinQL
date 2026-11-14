namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class RootTypeClass(string name, OperationType rootOperationType, LinQLTypesAs typesAs) : ComplexTypeClass(name, [], [], typesAs, canHaveIncludes: false)
{
    public OperationType RootOperation { get; } = rootOperationType;

    public override MemberDeclarationSyntax Create(SchemaModel model)
    {
        var rootType = (BaseTypeDeclarationSyntax)base.Create(model);

        rootType = rootType
            .AddBaseListTypes(SimpleBaseType(IdentifierName($"RootType<{this.Name}>")))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(
                    IdentifierName(nameof(OperationTypeAttribute).AttributeName()),
                    AttributeArgumentList(SingletonSeparatedList(AttributeArgument(IdentifierName($"{nameof(RootOperationType)}.{this.RootOperation}"))))))));

        return rootType;
    }
}
