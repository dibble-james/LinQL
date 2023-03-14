namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LinQL.Description;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class InterfaceRegistrationExtensionsClass : IClassFactory
{
    private readonly ILookup<string, string> types;

    public InterfaceRegistrationExtensionsClass(IEnumerable<ComplexTypeClass> types)
        => this.types = types.Where(t => t.Interfaces.Any())
        .SelectMany(t => t.Interfaces.Select(i => new { Interface = i, Type = t.Name }))
        .ToLookup(x => x.Interface, x => x.Type);

    public MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars) => ClassDeclaration(Identifier("JsonSerializerOptionsExtensions"))
        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
        .AddMembers(
            MethodDeclaration(ParseTypeName(nameof(JsonSerializerOptions)), Identifier("WithKnownInterfaces"))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .AddParameterListParameters(
                    Parameter(Identifier("options")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(nameof(JsonSerializerOptions))))
                .AddBodyStatements(this.types.Select(i => ParseStatement(@$"options.RegisterInterface<{i.Key}>({string.Join(", ", i.Select(t => $"typeof({t})"))});")).ToArray())
                .AddBodyStatements(ParseStatement("return options;")));
}
