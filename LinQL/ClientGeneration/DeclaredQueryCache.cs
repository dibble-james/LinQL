namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Location = Microsoft.CodeAnalysis.Location;
using SyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

/// <summary>
/// Emits the entity-cache hook of a declared query.
/// </summary>
/// <remarks>
/// The hook is generated from the document the declaration compiles to, so it knows exactly which fields the
/// operation selected. That is what <c>Write</c> needs: the properties a selection did not cover are
/// copied forward from the entity the store already holds, and filled with <c>default!</c> on a first write, which
/// is the merge a generated Strawberry Shake client performs. <c>Rebuild</c> replaces the entities the
/// operation selected with the stored instances and returns the root to project again.
/// An operation is cached only when its whole selection can be described this way: no inline fragment, and an
/// entity whose key field is not selected cannot be identified, so it takes the operation out of entity caching
/// rather than half-populating the store. A declaration that opts out keeps the operation-level cache.
/// </remarks>
internal sealed class DeclaredQueryCache
{
    private readonly SchemaModel model;
    private readonly string clientNamespace;
    private readonly string declaration;
    private readonly Location location;
    private readonly List<Diagnostic> problems = [];
    private int locals;

    private DeclaredQueryCache(SchemaModel model, string clientNamespace, string declaration, Location location)
        => (this.model, this.clientNamespace, this.declaration, this.location)
            = (model, clientNamespace, declaration, location);

    /// <summary>
    /// Emits the cache class of a declared query.
    /// </summary>
    /// <param name="document">The document the declaration translated to.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <param name="rootTypeName">The root operation type's name.</param>
    /// <param name="clientNamespace">The client namespace the schema declares.</param>
    /// <param name="name">The name of the class to emit.</param>
    /// <param name="declaration">The declaration, as it reads in a diagnostic.</param>
    /// <param name="location">The declaration's location.</param>
    /// <returns>
    /// The class, or <see langword="null"/> when the operation cannot be cached, and the reason it could not be if
    /// there is one to give.
    /// </returns>
    public static (ClassDeclarationSyntax? Class, IReadOnlyList<Diagnostic> Problems) Emit(
        DocumentNode document,
        SchemaModel model,
        string rootTypeName,
        string clientNamespace,
        string name,
        string declaration,
        Location location)
    {
        var selection = document.Definitions
            .OfType<OperationDefinitionNode>()
            .FirstOrDefault()
            ?.SelectionSet;

        if (selection is null)
        {
            return (null, []);
        }

        var emitter = new DeclaredQueryCache(model, clientNamespace, declaration, location);
        var fields = emitter.Fields(selection, rootTypeName);

        if (fields is null)
        {
            return (null, emitter.problems);
        }

        // A schema without @key has no entities to normalise, and a cache hook that stores nothing would only
        // add an empty class to the generated client.
        return fields.Any(x => emitter.ContainsEntities(x.Definition.Type.NamedType().Name.Value, x.Children))
            ? (emitter.Class(fields, rootTypeName, name), emitter.problems)
            : (null, emitter.problems);
    }

    /// <summary>
    /// Reads one selection set as the fields the cache code walks.
    /// </summary>
    /// <param name="set">The selection set.</param>
    /// <param name="typeName">The type the set is selected from.</param>
    /// <returns>The fields, or <see langword="null"/> when the set cannot be cached.</returns>
    private List<CacheField>? Fields(SelectionSetNode set, string typeName)
    {
        var fields = new List<CacheField>();

        foreach (var selection in set.Selections)
        {
            if (selection is not FieldNode field)
            {
                // An inline fragment selects a concrete type through an interface or union, which the generated
                // type does not model as one CLR type, so the operation opts out rather than caching a guess.
                this.Report("it selects through an inline fragment");
                return null;
            }

            if (field.Name.Value == "__typename")
            {
                continue;
            }

            var definition = this.model.FieldsOf(typeName).FirstOrDefault(x => x.Name.Value == field.Name.Value);

            if (definition is null)
            {
                this.Report($"'{field.Name.Value}' is not a field of '{typeName}'");
                return null;
            }

            var childType = definition.Type.NamedType().Name.Value;
            List<CacheField> children = [];

            if (field.SelectionSet is { } childSet)
            {
                if (!this.model.DeclaredTypesByName.TryGetValue(childType, out var declared)
                    || declared.Kind != DeclaredTypeKind.Object)
                {
                    this.Report($"'{childType}' is an interface or union, and only an object type can be an entity");
                    return null;
                }

                if (this.Fields(childSet, childType) is not { } nested)
                {
                    return null;
                }

                children = nested;
            }
            else if (!this.model.IsLeafType(childType))
            {
                this.Report($"'{field.Name.Value}' returns '{childType}' without selecting its fields");
                return null;
            }

            // An entity is identified by its key, so an operation that does not select the key cannot say which
            // entity it read, and writing an arbitrary instance would invent one.
            if (children.Count > 0
                && this.model.EntityKeys.TryGetValue(childType, out var key)
                && !children.Any(x => x.Definition.Name.Value == key.FieldName))
            {
                this.problems.Add(Diagnostic.Create(
                    EntityCacheDiagnostics.MissingEntityKey,
                    this.location,
                    this.declaration,
                    childType,
                    key.FieldName));

                return null;
            }

            fields.Add(new CacheField(definition, children, IsList(definition.Type)));
        }

        return fields;
    }

    private static bool IsList(ITypeNode type)
        => type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType());

    /// <summary>
    /// Records why the operation cannot be normalised.
    /// </summary>
    /// <param name="reason">The reason, written to follow the declaration in the message.</param>
    private void Report(string reason)
    {
        // The first failure ends the walk, so a nested set cannot add a second reason for the same operation.
        if (this.problems.Count == 0)
        {
            this.problems.Add(Diagnostic.Create(
                EntityCacheDiagnostics.NotNormalisable,
                this.location,
                this.declaration,
                reason));
        }
    }

    /// <summary>
    /// Emits the cache class.
    /// </summary>
    /// <param name="fields">The root's selected fields.</param>
    /// <param name="rootTypeName">The root operation type's name.</param>
    /// <param name="name">The name of the class to emit.</param>
    /// <returns>The class.</returns>
    private ClassDeclarationSyntax Class(IReadOnlyList<CacheField> fields, string rootTypeName, string name)
    {
        var root = ParseTypeName($"global::{this.clientNamespace}.{rootTypeName}");
        var className = name + "Cache";

        var write = new List<StatementSyntax>();

        foreach (var field in fields)
        {
            this.Value(Member(IdentifierName("root"), SchemaNames.FieldName(field.Definition.Name.Value)), field, rebuild: false, "writer", "w", write);
        }

        var rebuild = new List<StatementSyntax>();
        var rebuilt = new List<(string Property, ExpressionSyntax? Expression)>();

        foreach (var field in fields)
        {
            var property = SchemaNames.FieldName(field.Definition.Name.Value);
            var expression = this.Value(Member(IdentifierName("root"), property), field, rebuild: true, "reader", "r", rebuild);

            rebuilt.Add((property, expression));
        }

        rebuild.Add(ReturnStatement(
            ObjectCreationExpression(root).WithInitializer(InitializerExpression(
                SyntaxKind.ObjectInitializerExpression,
                SeparatedList([.. this.model.FieldsOf(rootTypeName).Select(x => Copied(x, rebuilt))])))));

        // Documentation is attached last: a node's leading trivia belongs to its first token, so adding the
        // modifiers afterwards would put the comment between them and the declaration's keyword.
        return ClassDeclaration(Identifier(className))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.SealedKeyword))
            .AddBaseListTypes(SimpleBaseType(ParseTypeName($"global::LinQL.Caching.LinQLOperationCache<{root}>")))
            .AddMembers(
                FieldDeclaration(VariableDeclaration(IdentifierName(className))
                        .AddVariables(VariableDeclarator(Identifier("Instance"))
                            .WithInitializer(EqualsValueClause(ImplicitObjectCreationExpression()))))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.ReadOnlyKeyword))
                    .WithLeadingTrivia(Doc("/// <summary>The shared instance.</summary>")),
                ConstructorDeclaration(Identifier(className))
                    .AddModifiers(Token(SyntaxKind.PrivateKeyword))
                    .WithBody(Block()),
                MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), Identifier("Write"))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.OverrideKeyword))
                    .AddParameterListParameters(
                        Parameter(Identifier("root")).WithType(root),
                        Parameter(Identifier("writer")).WithType(ParseTypeName("global::LinQL.Caching.IEntityWriter")))
                    .WithBody(Body(write))
                    .WithLeadingTrivia(Doc("/// <inheritdoc />")),
                MethodDeclaration(root, Identifier("Rebuild"))
                    .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.OverrideKeyword))
                    .AddParameterListParameters(
                        Parameter(Identifier("root")).WithType(root),
                        Parameter(Identifier("reader")).WithType(ParseTypeName("global::LinQL.Caching.IEntityReader")))
                    .WithBody(Body(rebuild))
                    .WithLeadingTrivia(Doc("/// <inheritdoc />")))
            .WithLeadingTrivia(Doc(
                "/// <summary>",
                "/// Writes and rebuilds the entities the declared query selects.",
                "/// </summary>",
                "/// <remarks>Generated from the declaration's document; do not edit.</remarks>"));
    }

    /// <summary>
    /// Emits the code that normalises one selected field's value.
    /// </summary>
    /// <param name="expression">The expression holding the value.</param>
    /// <param name="field">The selected field.</param>
    /// <param name="rebuild">Whether the code rebuilds rather than writes.</param>
    /// <param name="accessor">The writer or reader parameter's name.</param>
    /// <param name="prefix">The prefix of the locals emitted here.</param>
    /// <param name="body">The statements being emitted.</param>
    /// <returns>The expression holding the result, which is the input when nothing needs normalising.</returns>
    private ExpressionSyntax Value(
        ExpressionSyntax expression,
        CacheField field,
        bool rebuild,
        string accessor,
        string prefix,
        List<StatementSyntax> body)
        => this.Value(expression, field.Definition.Type.NamedType().Name.Value, field.Children, field.IsList, rebuild, accessor, prefix, body);

    private ExpressionSyntax Value(
        ExpressionSyntax expression,
        string typeName,
        IReadOnlyList<CacheField> children,
        bool isList,
        bool rebuild,
        string accessor,
        string prefix,
        List<StatementSyntax> body)
    {
        // A subtree with no entity in it needs no normalising: its fields are copied with the type that holds
        // them, and the store never sees them.
        if (!this.ContainsEntities(typeName, children))
        {
            return expression;
        }

        var value = this.Next(prefix);
        var normalised = new List<StatementSyntax>();

        if (isList)
        {
            var list = this.Next(prefix);
            var index = "i" + list;

            var items = new List<StatementSyntax>();
            var item = this.Value(ElementAccess(IdentifierName(value), IdentifierName(index)), typeName, children, isList: false, rebuild, accessor, prefix, items);

            items.Add(Assign(ElementAccess(IdentifierName(list), IdentifierName(index)), item));

            normalised.Add(Local(list, ArrayCreationExpression(ArrayType(
                ParseTypeName($"global::{this.clientNamespace}.{typeName}"),
                SingletonList(ArrayRankSpecifier(SingletonSeparatedList<ExpressionSyntax>(Member(IdentifierName(value), "Length"))))))));

            normalised.Add(For(index, Member(IdentifierName(value), "Length"), items));
            normalised.Add(Assign(IdentifierName(value), IdentifierName(list)));
        }
        else
        {
            this.Node(IdentifierName(value), typeName, children, rebuild, accessor, prefix, normalised);
        }

        body.Add(Local(value, expression));
        body.Add(If(IsNotNull(value), Body(normalised)));

        return IdentifierName(value);
    }

    /// <summary>
    /// Emits the code that writes or rebuilds one object value.
    /// </summary>
    /// <param name="value">The local holding the value, which is assigned the result.</param>
    /// <param name="typeName">The type's name.</param>
    /// <param name="children">The fields the selection covers.</param>
    /// <param name="rebuild">Whether the code rebuilds rather than writes.</param>
    /// <param name="accessor">The writer or reader parameter's name.</param>
    /// <param name="prefix">The prefix of the locals emitted here.</param>
    /// <param name="body">The statements being emitted.</param>
    private void Node(
        ExpressionSyntax value,
        string typeName,
        IReadOnlyList<CacheField> children,
        bool rebuild,
        string accessor,
        string prefix,
        List<StatementSyntax> body)
    {
        var type = ParseTypeName($"global::{this.clientNamespace}.{typeName}");
        var key = this.model.EntityKeys.TryGetValue(typeName, out var entityKey) ? entityKey : null;
        string? existing = null;

        if (rebuild)
        {
            if (key is not null)
            {
                var stored = this.Next(prefix);
                var current = this.Next(prefix);

                body.Add(If(
                    BinaryExpression(
                        SyntaxKind.LogicalAndExpression,
                        TryRead(accessor, typeName, Key(value, key), stored),
                        IsPattern(IdentifierName(stored), DeclarationPattern(type, SingleVariableDesignation(Identifier(current))))),
                    Body([Assign(value, IdentifierName(current))])));
            }
        }
        else if (key is not null)
        {
            var stored = this.Next(prefix);

            existing = this.Next(prefix);

            body.Add(ExpressionStatement(TryRead(accessor, typeName, Key(value, key), stored)));
            body.Add(Local(existing, BinaryExpression(SyntaxKind.AsExpression, IdentifierName(stored), type)));
        }

        var written = new List<(string Property, ExpressionSyntax? Expression)>();

        // A child is normalised before the object that holds it, so an entity nested in a selection is stored
        // before the instance referring to it.
        foreach (var child in children)
        {
            var property = SchemaNames.FieldName(child.Definition.Name.Value);
            var childType = child.Definition.Type.NamedType().Name.Value;

            if (!this.ContainsEntities(childType, child.Children))
            {
                continue;
            }

            written.Add((property, this.Value(Member(value, property), childType, child.Children, child.IsList, rebuild, accessor, prefix, body)));
        }

        // A rebuild only has to copy an instance when a nested entity changed under it, and a first write
        // already holds defaults for everything it did not select, so both keep the original reference when
        // there is nothing to merge. That keeps a stored instance identical to the one the response produced.
        var needed = rebuild ? written.Count > 0 : existing is not null || written.Count > 0;

        if (!needed)
        {
            if (!rebuild && key is not null)
            {
                body.Add(ExpressionStatement(Write(accessor, typeName, Key(value, key), value)));
            }

            return;
        }

        var initializer = new List<ExpressionSyntax>();

        foreach (var definition in this.model.FieldsOf(typeName))
        {
            var property = SchemaNames.FieldName(definition.Name.Value);
            var (_, childExpression) = written.FirstOrDefault(x => x.Property == property);

            ExpressionSyntax expression;

            if (childExpression is not null)
            {
                expression = childExpression;
            }
            else if (existing is not null && !children.Any(x => SchemaNames.FieldName(x.Definition.Name.Value) == property))
            {
                // The write did not select this property, so what the store holds is still the truth. A first
                // write has nothing to carry forward, and fills it with default! as a generated client does.
                expression = BinaryExpression(
                    SyntaxKind.CoalesceExpression,
                    ConditionalAccessExpression(IdentifierName(existing), MemberBindingExpression(IdentifierName(property))),
                    PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, LiteralExpression(SyntaxKind.DefaultLiteralExpression)));
            }
            else
            {
                expression = Member(value, property);
            }

            initializer.Add(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, IdentifierName(property), expression));
        }

        body.Add(Assign(value, ObjectCreationExpression(type).WithInitializer(
            InitializerExpression(SyntaxKind.ObjectInitializerExpression, SeparatedList(initializer)))));

        if (!rebuild && key is not null)
        {
            body.Add(ExpressionStatement(Write(accessor, typeName, Key(value, key), value)));
        }
    }

    /// <summary>
    /// Gets whether a selection normalises anything: an entity at this node, or one below it.
    /// </summary>
    /// <param name="typeName">The type's name.</param>
    /// <param name="children">The fields the selection covers.</param>
    /// <returns><see langword="true"/> when the subtree holds an entity.</returns>
    private bool ContainsEntities(string typeName, IReadOnlyList<CacheField> children)
        => this.model.EntityKeys.ContainsKey(typeName)
            || children.Any(x => this.ContainsEntities(x.Definition.Type.NamedType().Name.Value, x.Children));

    /// <summary>
    /// Emits one property of the root the rebuild returns: the expression the rebuild produced for it, or the
    /// value the root already held.
    /// </summary>
    /// <param name="definition">The root type's field.</param>
    /// <param name="rebuilt">The expressions the rebuild produced, by property.</param>
    /// <returns>The assignment.</returns>
    private static ExpressionSyntax Copied(FieldDefinitionNode definition, List<(string Property, ExpressionSyntax? Expression)> rebuilt)
    {
        var property = SchemaNames.FieldName(definition.Name.Value);
        var (_, expression) = rebuilt.FirstOrDefault(x => x.Property == property);

        return AssignmentExpression(
            SyntaxKind.SimpleAssignmentExpression,
            IdentifierName(property),
            expression ?? Member(IdentifierName("root"), property));
    }

    private string Next(string prefix) => prefix + this.locals++.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Emits a <c>var</c> declaration.
    /// </summary>
    /// <param name="name">The local's name.</param>
    /// <param name="value">The value it holds.</param>
    /// <returns>The declaration.</returns>
    private static LocalDeclarationStatementSyntax Local(string name, ExpressionSyntax value)
        => LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
            .AddVariables(VariableDeclarator(Identifier(name)).WithInitializer(EqualsValueClause(value))));

    /// <summary>
    /// Emits a <c>for</c> loop that walks a value by index, the way a generated client materialises a list.
    /// </summary>
    /// <param name="index">The index's name.</param>
    /// <param name="length">The expression holding the list's length.</param>
    /// <param name="body">The loop's statements.</param>
    /// <returns>The loop.</returns>
    private static ForStatementSyntax For(string index, ExpressionSyntax length, IReadOnlyList<StatementSyntax> body)
        => ForStatement(
            declaration: VariableDeclaration(IdentifierName("var"))
                .AddVariables(VariableDeclarator(Identifier(index))
                    .WithInitializer(EqualsValueClause(LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(0))))),
            initializers: default,
            condition: BinaryExpression(SyntaxKind.LessThanExpression, IdentifierName(index), length),
            incrementors: SingletonSeparatedList<ExpressionSyntax>(
                PostfixUnaryExpression(SyntaxKind.PostIncrementExpression, IdentifierName(index))),
            statement: Body(body));

    /// <summary>
    /// Emits a block holding the given statements.
    /// </summary>
    /// <param name="statements">The statements.</param>
    /// <returns>The block.</returns>
    private static BlockSyntax Body(IEnumerable<StatementSyntax> statements)
        => Block().AddStatements([.. statements]);

    private static ExpressionStatementSyntax Assign(ExpressionSyntax target, ExpressionSyntax value)
        => ExpressionStatement(AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value));

    private static ElementAccessExpressionSyntax ElementAccess(ExpressionSyntax expression, ExpressionSyntax index)
        => ElementAccessExpression(expression).AddArgumentListArguments(Argument(index));

    private static MemberAccessExpressionSyntax Member(ExpressionSyntax expression, string name)
        => MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expression, IdentifierName(name));

    private static IfStatementSyntax If(ExpressionSyntax condition, StatementSyntax statement)
        => IfStatement(condition, statement);

    private static BinaryExpressionSyntax IsNotNull(string name)
        => BinaryExpression(SyntaxKind.NotEqualsExpression, IdentifierName(name), LiteralExpression(SyntaxKind.NullLiteralExpression));

    private static IsPatternExpressionSyntax IsPattern(ExpressionSyntax expression, PatternSyntax pattern)
        => IsPatternExpression(expression, pattern);

    private static InvocationExpressionSyntax TryRead(string accessor, string typeName, ExpressionSyntax key, string stored)
        => Call(accessor, "TryRead", Argument(Text(typeName)), Argument(key), Out(stored));

    private static InvocationExpressionSyntax Write(string accessor, string typeName, ExpressionSyntax key, ExpressionSyntax entity)
        => Call(accessor, "Write", Argument(Text(typeName)), Argument(key), Argument(entity));

    private static InvocationExpressionSyntax Call(string accessor, string method, params ArgumentSyntax[] arguments)
        => InvocationExpression(Member(IdentifierName(accessor), method))
            .WithArgumentList(ArgumentList(SeparatedList(arguments)));

    private static ArgumentSyntax Out(string name)
        => Argument(DeclarationExpression(IdentifierName("var"), SingleVariableDesignation(Identifier(name))))
            .WithRefKindKeyword(Token(SyntaxKind.OutKeyword));

    /// <summary>
    /// Emits the key read of an entity value, which is asserted non-null the way a generated client does: the
    /// schema promises the key is there, and the value is only reached once a null check has passed.
    /// </summary>
    /// <param name="value">The entity.</param>
    /// <param name="key">The entity's key.</param>
    /// <returns>The key expression.</returns>
    private static ExpressionSyntax Key(ExpressionSyntax value, EntityKey key)
        => PostfixUnaryExpression(SyntaxKind.SuppressNullableWarningExpression, Member(value, SchemaNames.FieldName(key.FieldName)));

    private static LiteralExpressionSyntax Text(string value)
        => LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));

    /// <summary>
    /// Emits documentation comment trivia.
    /// </summary>
    /// <param name="lines">The comment's lines, each including its <c>///</c>.</param>
    /// <returns>The trivia.</returns>
    private static SyntaxTriviaList Doc(params string[] lines)
        => TriviaList([.. lines.SelectMany(line => new[] { Comment(line), EndOfLine("\n") })]);

    /// <summary>
    /// One selected field, and the fields its selection covers.
    /// </summary>
    /// <param name="Definition">The schema's field.</param>
    /// <param name="Children">The fields its selection covers, empty for a leaf.</param>
    /// <param name="IsList">Whether the field's type is a list.</param>
    private sealed record CacheField(FieldDefinitionNode Definition, IReadOnlyList<CacheField> Children, bool IsList);
}
