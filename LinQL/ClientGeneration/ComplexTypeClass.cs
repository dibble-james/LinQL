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

    public ComplexTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces, LinQLTypesAs typesAs = LinQLTypesAs.Class)
        => (this.Name, this.fields, this.Interfaces, this.TypesAs) = (name, fields.ToList(), interfaces, typesAs);

    public string Name { get; }

    public IEnumerable<string> Interfaces { get; }

    /// <summary>
    /// Gets the kind of declaration this type is emitted as.
    /// </summary>
    public LinQLTypesAs TypesAs { get; }

    public void WithFields(IReadOnlyList<FieldDefinitionNode> fields)
        => this.fields.AddRange(fields);

    protected virtual TypeDeclarationSyntax Type
    {
        get
        {
            var attributes = AttributeList(SeparatedList(
            [
                Attribute(IdentifierName(nameof(GraphQLTypeAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{this.Name}""")))))
            ]));

            // Record mode swaps the declaration keyword and the mutating accessor (see MutatorAccessor);
            // the members themselves are rendered the same way.
            if (this.TypesAs == LinQLTypesAs.Record)
            {
                return this.RecordType().AddAttributeLists(attributes);
            }

            return ClassDeclaration(Identifier(this.Name)).AddAttributeLists(attributes);
        }
    }

    /// <summary>
    /// Creates an empty <c>record</c> declaration for this type.
    /// </summary>
    /// <remarks>
    /// The brace tokens are supplied explicitly: the short factory overloads produce a declaration with
    /// missing tokens, which renders without a body once members are added.
    /// </remarks>
    private TypeDeclarationSyntax RecordType()
        => RecordDeclaration(
            kind: SyntaxKind.RecordDeclaration,
            attributeLists: default,
            modifiers: default,
            keyword: Token(SyntaxKind.RecordKeyword),
            classOrStructKeyword: default,
            identifier: Identifier(this.Name),
            typeParameterList: null,
            parameterList: null,
            baseList: null,
            constraintClauses: default,
            openBraceToken: Token(SyntaxKind.OpenBraceToken),
            members: default,
            closeBraceToken: Token(SyntaxKind.CloseBraceToken),
            semicolonToken: default);

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

    /// <summary>
    /// Gets the property's mutating accessor: <c>set</c> for a class and <c>init</c> for a record, so a
    /// generated record is only assigned while it is constructed, deserialised or copied with <c>with</c>.
    /// </summary>
    /// <remarks>
    /// <c>init</c> resolves to <c>System.Runtime.CompilerServices.IsExternalInit</c>, which .NET 5+ and the
    /// usual polyfill packages provide. Interface members get the same accessor, because a record with an
    /// init-only property cannot implement an interface that declares a setter.
    /// </remarks>
    protected AccessorDeclarationSyntax MutatorAccessor
        => AccessorDeclaration(this.TypesAs == LinQLTypesAs.Record ? SyntaxKind.InitAccessorDeclaration : SyntaxKind.SetAccessorDeclaration)
            .WithSemicolonToken(Token(SyntaxKind.SemicolonToken));

    protected virtual Func<FieldDefinitionNode, PropertyDeclarationSyntax> CreateField(IDictionary<string, Scalar> knownScalars)
        => f => PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
            .AddAccessorListAccessors(this.MutatorAccessor);

    protected virtual Func<FieldDefinitionNode, IEnumerable<MemberDeclarationSyntax>> CreateOperation(IDictionary<string, Scalar> knownScalars)
        => f =>
        [
            PropertyDeclaration(ParseTypeName(TypeName(f.Type, knownScalars)), Identifier(FieldName(f.Name.Value)))
                    .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
                    .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
                    .AddAccessorListAccessors(this.MutatorAccessor)
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

    internal static bool IsFilterInputType(string typeName)
        => typeName.Length > FilterInputSuffix.Length && typeName.EndsWith(FilterInputSuffix, StringComparison.Ordinal);

    internal static string FilterBuilderName(string filterTypeName)
        => filterTypeName + "Builder";
}
