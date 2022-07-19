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
    private readonly IEnumerable<string> interfaces;

    public ComplexTypeClass(string name)
        : this(name, Enumerable.Empty<FieldDefinitionNode>(), Enumerable.Empty<string>())
    {
    }

    public ComplexTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces)
        => (this.Name, this.fields, this.interfaces) = (name, fields.ToList(), interfaces);

    public string Name { get; }

    public void WithFields(IReadOnlyList<FieldDefinitionNode> fields)
        => this.fields.AddRange(fields);

    protected virtual TypeDeclarationSyntax Type => ClassDeclaration(Identifier(this.Name));

    public virtual MemberDeclarationSyntax Create()
    {
        var type = this.Type
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.Properties.Select(f =>
                PropertyDeclaration(ParseTypeName(TypeName(f.Type)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))))
                .ToArray())
            .AddMembers(this.Methods.SelectMany(this.CreateOperation).ToArray());

        if (this.interfaces.Any())
        {
            return type.AddBaseListTypes(this.interfaces.Select(x => SimpleBaseType(IdentifierName(x))).ToArray());
        }

        return type;
    }

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

    protected static string TypeName(ITypeNode type)
    {
        var typeName = TypeMapping.TryGetValue(type.NamedType().Name.Value, out var mapped)
            ? mapped : type.NamedType().Name.Value;

        typeName = type.IsListType() || type.IsNonNullType() && type.InnerType().IsListType() ? typeName + "[]" : typeName;

        return type.IsNonNullType() ? typeName : typeName + "?";
    }

    protected static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);

    protected virtual IEnumerable<MemberDeclarationSyntax> CreateOperation(FieldDefinitionNode f)
        => new MemberDeclarationSyntax[]
        {
            PropertyDeclaration(ParseTypeName(TypeName(f.Type)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .WithInitializer(EqualsValueClause(ParseExpression("null!")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            MethodDeclaration(ParseTypeName(TypeName(f.Type)), Identifier("Execute" + FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SeparatedList(new[]
                    {
                        Attribute(IdentifierName(nameof(GraphQLOperationAttribute).AttributeName())),
                        Attribute(IdentifierName(nameof(GraphQLFieldAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{f.Name.Value}""")))))
                    })))
                    .AddParameterListParameters(
                        f.Arguments.Select(p => Parameter(Identifier(p.Name.Value)).WithType(ParseTypeName(TypeName(p.Type)))).ToArray())
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression(FieldName(f.Name.Value))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
        };
}
