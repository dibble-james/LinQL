namespace LinQL.ClientGeneration;

using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal interface IClassFactory
{
    MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars);
}
