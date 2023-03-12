namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class RootTypeClass : ComplexTypeClass
{
    public RootTypeClass(string name, OperationType rootOperationType)
        : base(name) => this.RootOperation = rootOperationType;

    public OperationType RootOperation { get; }

    public override MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
    {
        var rootType = (base.Create(knownScalars) as ClassDeclarationSyntax)!
            .AddBaseListTypes(SimpleBaseType(IdentifierName($"RootType<{this.Name}>")))
            .AddAttributeLists(AttributeList(SingletonSeparatedList(
                Attribute(
                    IdentifierName(nameof(OperationTypeAttribute).AttributeName()),
                    AttributeArgumentList(SingletonSeparatedList(AttributeArgument(IdentifierName($"{nameof(RootOperationType)}.{this.RootOperation}"))))))));

        return rootType;
    }
}
