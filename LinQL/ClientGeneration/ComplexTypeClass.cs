namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using LinQL.Expressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ComplexTypeClass : IClassFactory
{
    private readonly List<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name)
        : this(name, Enumerable.Empty<FieldDefinitionNode>(), Enumerable.Empty<string>())
    {
    }

    public ComplexTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces)
        => (this.Name, this.fields, this.Interfaces) = (name, fields.ToList(), interfaces);

    public string Name { get; }

    public IEnumerable<string> Interfaces { get; }

    public void WithFields(IReadOnlyList<FieldDefinitionNode> fields)
        => this.fields.AddRange(fields);

    protected virtual TypeDeclarationSyntax Type => ClassDeclaration(Identifier(this.Name));

    public virtual MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
    {
        var type = this.Type
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers(this.Properties.Select(this.CreateField(knownScalars)).ToArray())
            .AddMembers(this.Methods.SelectMany(this.CreateOperation(knownScalars)).ToArray());

        if (this.Interfaces.Any())
        {
            return type.AddBaseListTypes(this.Interfaces.Select(x => SimpleBaseType(IdentifierName(x))).ToArray());
        }

        return type;
    }

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

    protected static string TypeName(ITypeNode type, IDictionary<string, Scalar> knownScalars)
    {
        var typeName = knownScalars.TryGetValue(type.NamedType().Name.Value, out var mapped)
            ? mapped.RuntimeType : type.NamedType().Name.Value;

        typeName = type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType()) ? typeName + "[]" : typeName;

        return type.IsNonNullType() ? typeName : typeName + "?";
    }

    protected static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);

    protected virtual Func<FieldDefinitionNode, PropertyDeclarationSyntax> CreateField(IDictionary<string, Scalar> knownScalars)
        => f => PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

    protected virtual Func<FieldDefinitionNode, IEnumerable<MemberDeclarationSyntax>> CreateOperation(IDictionary<string, Scalar> knownScalars)
        => f => new MemberDeclarationSyntax[]
        {
            PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .WithInitializer(EqualsValueClause(ParseExpression("null!")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            MethodDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier("Execute" + FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SeparatedList(new[]
                    {
                        Attribute(IdentifierName(nameof(GraphQLOperationAttribute).AttributeName())),
                        Attribute(IdentifierName(nameof(GraphQLFieldAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{f.Name.Value}""")))))
                    })))
                    .AddParameterListParameters(
                        f.Arguments.Select(p => Parameter(Identifier(p.Name.Value))
                            .WithType(ParseTypeName(TypeName(p.Type, knownScalars)))
                            .AddAttributeLists(AttributeList(SeparatedList(new[]
                            {
                                Attribute(IdentifierName(nameof(GraphQLArgumentAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"GQLType = ""{p.Type.NamedType().Name.Value}{(p.Type.IsNonNullType() ? "!" : string.Empty)}""")))))
                            })))).ToArray())
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression(FieldName(f.Name.Value))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
        };
}
