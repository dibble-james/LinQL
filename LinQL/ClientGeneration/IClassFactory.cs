namespace LinQL.ClientGeneration;

using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal interface IClassFactory
{
    internal MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars);
}
