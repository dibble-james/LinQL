namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal interface IClassFactory
{
    internal MemberDeclarationSyntax Create(SchemaModel model);
}
