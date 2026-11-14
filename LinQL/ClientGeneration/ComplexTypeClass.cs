namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

internal class ComplexTypeClass : IClassFactory
{
    private readonly List<FieldDefinitionNode> fields;

    public ComplexTypeClass(string name, IEnumerable<FieldDefinitionNode> fields, IEnumerable<string> interfaces, LinQLTypesAs typesAs = LinQLTypesAs.Class, bool canHaveIncludes = true)
        => (this.Name, this.fields, this.Interfaces, this.TypesAs, this.CanHaveIncludes) = (name, fields.ToList(), interfaces, typesAs, canHaveIncludes);

    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether the generated type is one a query can select from.
    /// </summary>
    /// <remarks>
    /// The type implements <see cref="ICanHaveIncludes"/> when this is set, which is what the constraint on
    /// <c>Includes</c> checks: a root type, an input object or a scalar does not offer the extension.
    /// </remarks>
    public bool CanHaveIncludes { get; }

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

    public virtual MemberDeclarationSyntax Create(SchemaModel model)
    {
        var type = this.Type
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
            .AddMembers([.. this.Properties.Select(this.CreateField(model))])
            .AddMembers([.. this.Methods.SelectMany(this.CreateOperation(model))]);

        var baseTypes = this.Interfaces
            .Concat(this.CanHaveIncludes ? [nameof(ICanHaveIncludes)] : [])
            .Select(x => (BaseTypeSyntax)SimpleBaseType(IdentifierName(x)))
            .ToArray();

        return baseTypes.Length > 0 ? type.AddBaseListTypes(baseTypes) : type;
    }

    private IEnumerable<FieldDefinitionNode> Properties
        => this.fields.Where(x => !x.Arguments.Any());

    private IEnumerable<FieldDefinitionNode> Methods
        => this.fields.Where(x => x.Arguments.Any());

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

    protected Func<FieldDefinitionNode, PropertyDeclarationSyntax> CreateField(SchemaModel model)
        => f => this.CreateProperty(f, model, initialise: false);

    /// <summary>
    /// Gets a value indicating whether the type implements the field commands or only declares them.
    /// </summary>
    /// <remarks>
    /// A class initialises the property paired with an <c>Execute*</c> method and gives that method an
    /// expression body; an interface declares the members' shapes, and either of those would not
    /// compile there.
    /// </remarks>
    protected virtual bool ImplementsCommands => true;

    protected Func<FieldDefinitionNode, IEnumerable<MemberDeclarationSyntax>> CreateOperation(SchemaModel model)
        => f =>
        [
            this.CreateCommandProperty(f, model),
            this.CreateCommandMethod(f, model),
        ];

    /// <summary>
    /// Creates the property that holds the value an <c>Execute*</c> method returns.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="model">The schema.</param>
    /// <returns>The property declaration.</returns>
    private PropertyDeclarationSyntax CreateCommandProperty(FieldDefinitionNode field, SchemaModel model)
        => this.CreateProperty(field, model, initialise: this.ImplementsCommands);

    /// <summary>
    /// Creates the property that holds a field's value.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="model">The schema.</param>
    /// <param name="initialise">Whether the property starts as <see langword="null"/>.</param>
    /// <returns>The property declaration.</returns>
    private PropertyDeclarationSyntax CreateProperty(FieldDefinitionNode field, SchemaModel model, bool initialise)
    {
        var property = PropertyDeclaration(ParseTypeName(model.TypeName(field.Type)), Identifier(SchemaNames.FieldName(field.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(Token(SyntaxKind.SemicolonToken)))
            .AddAccessorListAccessors(this.MutatorAccessor);

        return initialise
            ? property.WithInitializer(EqualsValueClause(ParseExpression("null!"))).WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            : property;
    }

    /// <summary>
    /// Creates the <c>Execute*</c> method that calls a field with arguments.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="model">The schema.</param>
    /// <returns>The method declaration.</returns>
    private MethodDeclarationSyntax CreateCommandMethod(FieldDefinitionNode field, SchemaModel model)
    {
        var method = MethodDeclaration(ParseTypeName(model.TypeName(field.Type)), Identifier("Execute" + SchemaNames.FieldName(field.Name.Value)))
            .WithModifiers(TokenList(Token(SyntaxKind.PublicKeyword)))
            .AddAttributeLists(AttributeList(SeparatedList(
            [
                Attribute(IdentifierName(nameof(GraphQLOperationAttribute).AttributeName())),
                Attribute(IdentifierName(nameof(GraphQLFieldAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"Name = ""{field.Name.Value}""")))))
            ])))
            .AddParameterListParameters(
                [.. field.Arguments.Select(p => Parameter(SchemaNames.IdentifierFor(p.Name.Value))
                    .WithType(ParseTypeName(model.TypeName(p.Type)))
                    .AddAttributeLists(AttributeList(SeparatedList(
                    [
                        Attribute(IdentifierName(nameof(GraphQLArgumentAttribute).AttributeName()), AttributeArgumentList(SingletonSeparatedList(AttributeArgument(ParseExpression(@$"GQLType = ""{p.Type}""")))))
                    ]))))]);

        return this.ImplementsCommands
            ? method.WithExpressionBody(ArrowExpressionClause(ParseExpression(SchemaNames.FieldName(field.Name.Value))))
                .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
            : method.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
    }
}
