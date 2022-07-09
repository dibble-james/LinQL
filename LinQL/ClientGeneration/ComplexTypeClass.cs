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

    private readonly List<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name, IReadOnlyList<FieldDefinitionNode> fields)
        => (this.Name, this.fields) = (name, fields.ToList());

    public string Name { get; }

    public void WithFields(IReadOnlyList<FieldDefinitionNode> fields)
        => this.fields.AddRange(fields);

    public virtual MemberDeclarationSyntax Create()
        => ClassDeclaration(Identifier(this.Name))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.Properties.Select(f =>
                PropertyDeclaration(ParseTypeName(TypeName(f.Type.NamedType().Name.Value)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))))
                .ToArray())
            .AddMembers(this.Methods.SelectMany(CreateOperation).ToArray());

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

    private static string TypeName(string type) =>
        TypeMapping.TryGetValue(type, out var mapped) ? mapped : type;

    private static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);

    private static IEnumerable<MemberDeclarationSyntax> CreateOperation(FieldDefinitionNode f)
        => new MemberDeclarationSyntax[]
        {
            PropertyDeclaration(ParseTypeName(TypeName(f.Type.NamedType().Name.Value)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .WithInitializer(EqualsValueClause(ParseExpression("null!")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            MethodDeclaration(ParseTypeName(TypeName(f.Type.NamedType().Name.Value)), Identifier("Execute" + FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SeparatedList(new[]
                    {
                        Attribute(IdentifierName(nameof(GraphQLOperationAttribute))),
                        Attribute(IdentifierName(nameof(GraphQLFieldAttribute)), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{f.Name.Value}""")))))
                    })))
                    .AddParameterListParameters(
                        f.Arguments.Select(p => Parameter(Identifier(p.Name.Value)).WithType(ParseTypeName(TypeName(p.Type.NamedType().Name.Value)))).ToArray())
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression(FieldName(f.Name.Value))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
        };
}
