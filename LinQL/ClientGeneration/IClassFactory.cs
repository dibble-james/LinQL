namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal interface IClassFactory
{
    MemberDeclarationSyntax Create();
}
