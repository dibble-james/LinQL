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

    public string Translate(GraphQLExpression<TRoot, TData> query)
    {
        this.Visit(query);

        var operation = new OperationDefinitionNode(
            location: null,
            name: new NameNode("linql"),
            description: null,
            operation: GetOperationType(query.RootOperation.Type),
            variableDefinitions: [.. query.Variables.Select(ToVariableDefinition)],
            directives: [],
            selectionSet: this.root!);

        // Hot Chocolate's own writer renders the document; there is no LinQL renderer to keep in
        // step with the model any more.
        return new DocumentNode([operation]).ToString();
    }

    protected override Expression VisitExtension(Expression node) => node switch
    {
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

        // __typename is part of the document rather than a rendering rule, so the path that builds
        // the document puts it where the document needs it and the renderer stays type-free.
        if (field.Type.RequiresTypeName())
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
