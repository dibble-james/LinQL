namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class RootTypeClass(string name, OperationType rootOperationType, LinQLTypesAs typesAs) : ComplexTypeClass(name, [], [], typesAs)
{
    public OperationType RootOperation { get; } = rootOperationType;

    public override MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
    {
        var rootType = (BaseTypeDeclarationSyntax)base.Create(knownScalars);

        rootType = rootType
            .AddBaseListTypes(SimpleBaseType(IdentifierName($"RootType<{this.Name}>")))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(
                    IdentifierName(nameof(OperationTypeAttribute).AttributeName()),
                    AttributeArgumentList(SingletonSeparatedList(AttributeArgument(IdentifierName($"{nameof(RootOperationType)}.{this.RootOperation}"))))))));

        return rootType;
    }
}
