namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using LinQL.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ComplexTypeClass : IClassFactory
{
    private static readonly Dictionary<string, string> TypeMapping = new(StringComparer.InvariantCultureIgnoreCase)
    {
        { "Int", "int" },
        { "Float", "float" },
        { "String", "string" },
        { "ID", "string" },
        { "Date", "System.DateTime" },
        { "Boolean", "bool" },
        { "Long", "long" },
        { "uuid", "System.Guid" },
        { "timestamptz", "System.DateTimeOffset" },
        { "Uri", "System.Uri" }
    };

    private readonly IReadOnlyList<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name, IReadOnlyList<FieldDefinitionNode> fields)
        => (this.Name, this.fields) = (name, fields);

    public string Name { get; }

    public virtual MemberDeclarationSyntax Create()
        => ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.Properties.Select(f =>
                PropertyDeclaration(ParseTypeName(TypeName(f.Type.NamedType().Name.Value)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))))
                .ToArray())
            .AddMembers(this.Methods.Select(f =>
                MethodDeclaration(ParseTypeName(TypeName(f.Type.NamedType().Name.Value)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SingletonSeparatedList(Attribute(IdentifierName(nameof(GraphQLOperationAttribute))))))
                    .AddParameterListParameters(
                        f.Arguments.Select(p => Parameter(Identifier(p.Name.Value)).WithType(ParseTypeName(TypeName(p.Type.NamedType().Name.Value)))).ToArray())
                    .WithBody(Block(ParseStatement("return default!;"))))
                .ToArray());

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

    private static string TypeName(string type) =>
        TypeMapping.TryGetValue(type, out var mapped) ? mapped : type;

    private static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);
}
