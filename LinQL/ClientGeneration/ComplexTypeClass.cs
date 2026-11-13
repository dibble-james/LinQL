namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using LinQL.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ComplexTypeClass : IClassFactory
{
    internal const string FilterInputSuffix = "FilterInput";

    private readonly List<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name)
        : this(name, [], [])
    {
    }

    public ComplexTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces)
        => (this.Name, this.fields, this.Interfaces) = (name, fields.ToList(), interfaces);

    public string Name { get; }

    public IEnumerable<string> Interfaces { get; }

    public void WithFields(IReadOnlyList<FieldDefinitionNode> fields)
        => this.fields.AddRange(fields);

    protected virtual TypeDeclarationSyntax Type => ClassDeclaration(Identifier(this.Name))
            .AddAttributeLists([AttributeList(SeparatedList(
            [
                Attribute(IdentifierName(nameof(GraphQLTypeAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{this.Name}""")))))
            ]))]);

    public virtual MemberDeclarationSyntax Create(IDictionary<string, Scalar> knownScalars)
    {
        var type = this.Type
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers([.. this.Properties.Select(this.CreateField(knownScalars))])
            .AddMembers([.. this.Methods.SelectMany(this.CreateOperation(knownScalars))]);

        if (this.Interfaces.Any())
        {
            return type.AddBaseListTypes([.. this.Interfaces.Select(x => SimpleBaseType(IdentifierName(x)))]);
        }

        return type;
    }

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

    internal static string TypeName(ITypeNode type, IDictionary<string, Scalar> knownScalars)
    {
        var typeName = knownScalars.TryGetValue(type.NamedType().Name.Value, out var mapped)
            ? mapped.RuntimeType : type.NamedType().Name.Value;

        typeName = type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType()) ? typeName + "[]" : typeName;

        return type.IsNonNullType() ? typeName : typeName + "?";
    }

    internal static string FieldName(string field)
        => char.ToUpperInvariant(field.First()) + field.ToCamelCase().Substring(1);

    protected virtual Func<FieldDefinitionNode, PropertyDeclarationSyntax> CreateField(IDictionary<string, Scalar> knownScalars)
        => f => PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)));

    protected virtual Func<FieldDefinitionNode, IEnumerable<MemberDeclarationSyntax>> CreateOperation(IDictionary<string, Scalar> knownScalars)
        => f => [.. this.CreateOperationMembers(f, knownScalars), .. CreateFilterCompanion(f, knownScalars)];

    protected virtual IEnumerable<MemberDeclarationSyntax> CreateOperationMembers(FieldDefinitionNode f, IDictionary<string, Scalar> knownScalars)
        =>
        [
            PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .WithInitializer(EqualsValueClause(ParseExpression("null!")))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
            MethodDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier("Execute" + FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAttributeLists(AttributeList(SeparatedList(
                    [
                        Attribute(IdentifierName(nameof(GraphQLOperationAttribute).AttributeName())),
                        Attribute(IdentifierName(nameof(GraphQLFieldAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{f.Name.Value}""")))))
                    ])))
                    .AddParameterListParameters(
                        [.. f.Arguments.Select(p => Parameter(IdentifierFor(p.Name.Value))
                            .WithType(ParseTypeName(TypeName(p.Type, knownScalars)))
                            .AddAttributeLists(AttributeList(SeparatedList(
                            [
                                Attribute(IdentifierName(nameof(GraphQLArgumentAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"GQLType = ""{p.Type.ToString()}""")))))
                            ]))))])
                    .WithExpressionBody(ArrowExpressionClause(ParseExpression(FieldName(f.Name.Value))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken)),
        ];

    internal static SyntaxToken IdentifierFor(string name)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
            ? VerbatimIdentifier(SyntaxTriviaList.Empty, name, name, SyntaxTriviaList.Empty)
            : Identifier(name);

    private static IEnumerable<MemberDeclarationSyntax> CreateFilterCompanion(FieldDefinitionNode field, IDictionary<string, Scalar> knownScalars)
        => field.Arguments.FirstOrDefault(x => IsFilterInputType(x.Type.NamedType().Name.Value)) is { } filterArgument
            ? [CreateFilterCompanionMember(field, filterArgument, knownScalars)]
            : [];

    internal static bool IsFilterInputType(string typeName)
        => typeName.Length > FilterInputSuffix.Length && typeName.EndsWith(FilterInputSuffix, StringComparison.Ordinal);

    internal static string FilterBuilderName(string filterTypeName)
        => filterTypeName + "Builder";

    private static MethodDeclarationSyntax CreateFilterCompanionMember(FieldDefinitionNode field, InputValueDefinitionNode filterArgument, IDictionary<string, Scalar> knownScalars)
        => MethodDeclaration(ParseTypeName(TypeName(field.Type, knownScalars)), Identifier("Filter" + FieldName(field.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(AttributeList(SeparatedList(
            [
                Attribute(
                    IdentifierName(nameof(GraphQLFilterAttribute).AttributeName()),
                    AttributeArgumentList(SeparatedList(
                    [
                        AttributeArgument(ParseExpression($@"Field = ""{field.Name.Value}""")),
                        AttributeArgument(ParseExpression($@"Argument = ""{filterArgument.Name.Value}""")),
                        AttributeArgument(ParseExpression($@"GQLType = ""{filterArgument.Type}""")),
                    ])))
            ])))
            .AddParameterListParameters(
                Parameter(Identifier("_")).WithType(ParseTypeName($"System.Linq.Expressions.Expression<System.Func<{FilterBuilderName(filterArgument.Type.NamedType().Name.Value)}, {FilterBuilderName(filterArgument.Type.NamedType().Name.Value)}>>")))
            .WithExpressionBody(ArrowExpressionClause(ParseExpression(FieldName(field.Name.Value))))
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
}
