namespace LinQL.ClientGeneration;

using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class InterfaceRegistrationExtensionsClass : IClassFactory
{
    public MemberDeclarationSyntax Create(SchemaModel model) => ClassDeclaration(Identifier("JsonSerializerOptionsExtensions"))
        .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
        .AddMembers(
            MethodDeclaration(ParseTypeName(nameof(JsonSerializerOptions)), Identifier("WithKnownInterfaces"))
                .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword)))
                .AddParameterListParameters(
                    Parameter(Identifier("options")).WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword))).WithType(ParseTypeName(nameof(JsonSerializerOptions))))
                .AddBodyStatements([.. Registrations(model).Select(i => ParseStatement(@$"options.RegisterInterface<{i.Key}>({string.Join(", ", i.Select(t => $"typeof({t})"))});"))])
                .AddBodyStatements(ParseStatement("return options;")));

    /// <summary>
    /// Gets the types that implement each interface, keyed by interface name.
    /// </summary>
    /// <param name="model">The schema.</param>
    /// <returns>The implementing types per interface, in document order.</returns>
    private static ILookup<string, string> Registrations(SchemaModel model)
        => model.Types.OfType<ComplexTypeClass>()
            .Where(type => type.Interfaces.Any())
            .SelectMany(type => type.Interfaces.Select(name => new { Interface = name, Type = type.Name }))
            .ToLookup(x => x.Interface, x => x.Type);
}
