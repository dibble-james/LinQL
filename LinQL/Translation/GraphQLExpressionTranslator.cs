namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Text.RegularExpressions;
using HotChocolate.Language;
using LinQL.Description;
using LinQL.Documents;
using LinQL.Expressions;

/// <summary>
/// Translates <see cref="GraphQLExpression{TRoot, TResult}"/>s.
/// </summary>
public static class GraphQLExpressionTranslator
{
    /// <summary>
    /// Extract a <see cref="LinqQLRequest{TRoot, TData}"/> from the given <paramref name="expression"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type.</typeparam>
    /// <typeparam name="TData">The result type.</typeparam>
    /// <param name="expression">The expression to translate.</param>
    /// <returns>The request.</returns>
    public static LinqQLRequest<TRoot, TData> Translate<TRoot, TData>(GraphQLExpression<TRoot, TData> expression)
        where TRoot : RootType<TRoot>
    {
        var translator = new GraphQLExpressionTranslator<TRoot, TData>();

        var query = translator.Translate(expression);

        return new(expression, query, expression.Variables.ToDictionary(x => x.Name, x => x.Value));
    }
}

internal class GraphQLExpressionTranslator<TRoot, TData> : ExpressionVisitor
    where TRoot : RootType<TRoot>
{
    private SelectionSetNode? root;
    private List<ISelectionNode>? selections;
    private bool emitRootTypeName;

    public string Translate(GraphQLExpression<TRoot, TData> query)
    {
        var operationType = GetOperationType(query.RootOperation.Type);

        // A subscription may not select __typename at its root (GraphQL spec §4.1 and
        // §5.2.4.1), so this is the one exception the spec forces on the policy.
        this.emitRootTypeName = operationType != OperationType.Subscription;

        this.Visit(query);

        var operation = new OperationDefinitionNode(
            location: null,
            name: new NameNode("linql"),
            description: null,
            operation: operationType,
            variableDefinitions: [.. query.Variables.Select(ToVariableDefinition)],
            directives: [],
            selectionSet: this.root!);

        return new DocumentNode([operation]).ToString();
    }

    protected override Expression VisitExtension(Expression node) => node switch
    {
        SpreadExpression spread => this.VisitSpread(spread),
        TypeFieldExpression field => this.VisitField(field),
        ScalarFieldExpression scalar => this.VisitScalar(scalar),
        _ => base.VisitExtension(node),
    };

    private static OperationType GetOperationType(RootOperationType type) => type switch
    {
        RootOperationType.Mutation => OperationType.Mutation,
        RootOperationType.Subscription => OperationType.Subscription,
        RootOperationType.Query => OperationType.Query,
        _ => throw new NotSupportedException(),
    };

    private static VariableDefinitionNode ToVariableDefinition(Variable variable) => new(
        location: null,
        variable: new VariableNode(variable.Name),
        description: null,
        type: TypeReferenceParser.Parse(variable.Type),
        defaultValue: null,
        directives: []);

    private static FieldNode ToField(FieldExpression field, SelectionSetNode? selectionSet) => new(
        name: new NameNode(field.FieldName.ToCamelCase()),
        alias: null,
        directives: [],
        arguments: [.. field.Arguments.Select(argument => new ArgumentNode(argument.Key, new VariableNode(argument.Value)))],
        selectionSet: selectionSet);

    private Expression VisitField(TypeFieldExpression field)
    {
        var selections = new List<ISelectionNode>();
        var isRoot = this.selections is null;

        // Every non-scalar selection carries __typename, the root included. The policy is
        // unconditional: a cache entry written by a runtime query has to be identifiable by type,
        // and a configurable document shape would make cache keys depend on client configuration.
        var typeName = isRoot
            ? this.emitRootTypeName
            : !field.Type.GetItemType().IsScalar(field.Root.Scalars);

        if (typeName)
        {
            selections.Add(new FieldNode("__typename"));
        }

        var parent = this.selections;
        this.selections = selections;

        try
        {
            base.VisitExtension(field);
        }
        finally
        {
            this.selections = parent;
        }

        var selectionSet = new SelectionSetNode(selections);

        // The root is the operation, not a field: its children are the operation's selection set, so
        // it is recorded rather than added to a parent.
        if (parent is null)
        {
            this.root = selectionSet;
        }
        else
        {
            parent.Add(ToField(field, selectionSet));
        }

        return field;
    }

    /// <summary>
    /// Writes a spread as the inline fragment it is.
    /// </summary>
    /// <remarks>
    /// A fragment carries <c>__typename</c> for the same reason a field does: the type of a value selected
    /// through a fragment is only known at runtime.
    /// </remarks>
    private Expression VisitSpread(SpreadExpression spread)
    {
        var parent = this.selections
            ?? throw new InvalidOperationException($"The spread of '{spread.Type.Name}' has to be selected inside a field.");

        var selections = new List<ISelectionNode> { new FieldNode("__typename") };
        this.selections = selections;

        try
        {
            base.VisitExtension(spread);
        }
        finally
        {
            this.selections = parent;
        }

        parent.Add(new InlineFragmentNode(
            location: null,
            typeCondition: new NamedTypeNode(spread.TypeCondition.GetTypeName()),
            directives: [],
            selectionSet: new SelectionSetNode(selections)));

        return spread;
    }

    private Expression VisitScalar(ScalarFieldExpression scalar)
    {
        this.selections!.Add(ToField(scalar, selectionSet: null));

        return scalar;
    }
}


internal static class Extentions
{
    private static readonly Regex PropertyRegex = new(@"""([\w|\d]+)"":", RegexOptions.Compiled);

    public static string UnquotePropertyNames(this string s) => PropertyRegex.Replace(s, "$1:");
}
