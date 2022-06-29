namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using LinQL.Description;
using LinQL.Expressions;

/// <summary>
/// An <see cref="ExpressionVisitor"/> to convert a <see cref="Expression"/> to a <see cref="GraphQLExpression{TRoot, TResult}"/>.
/// </summary>
public class ExpressionTranslator : ExpressionVisitor
{
    private readonly TypeFieldExpression expression;

    private ExpressionTranslator(TypeFieldExpression root)
        => this.expression = root;

    /// <summary>
    /// Convert a .Net <see cref="Expression"/> to a <see cref="GraphQLExpression{TRoot, TResult}"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type.</typeparam>
    /// <typeparam name="TData">The query result.</typeparam>
    /// <param name="graph">The graph to execute the query against.</param>
    /// <param name="query">The query to translate.</param>
    /// <returns>The translated expression.</returns>
    /// <exception cref="InvalidOperationException">No <see cref="OperationTypeAttribute"/> is defined on <typeparamref name="TRoot"/></exception>
    public static GraphQLExpression<TRoot, TData> Translate<TRoot, TData>(Graph graph, Expression<Func<TRoot, TData>> query)
    {
        var operationType = typeof(TRoot).GetCustomAttribute<OperationTypeAttribute>()?.Operation
            ?? throw new InvalidOperationException($"{typeof(TRoot)} does not have an {nameof(OperationTypeAttribute)} defined");

        var root = new GraphQLExpression<TRoot, TData>(graph, operationType, query);
        var translator = new ExpressionTranslator(root);
        translator.Visit(query.Body);
        return root;
    }

    /// <inheritdoc/>
    protected override Expression VisitMember(MemberExpression node) => node switch
    {
        { Member: FieldInfo field, Expression: object } => this.TraverseMember(node.Expression).WithField(field.ToField()),
        { Member: FieldInfo field } => this.expression.WithField(field.ToField()),
        { Member: PropertyInfo property, Expression: object } => this.TraverseMember(node.Expression).WithField(property.ToField()),
        { Member: PropertyInfo property } => this.expression.WithField(property.ToField()),
        _ => base.VisitMember(node),
    };

    /// <inheritdoc/>
    protected override Expression VisitMethodCall(MethodCallExpression node) => node switch
    {
        { Object: object, Method: var method } when method.IsOperation() => this.TraverseMember(node.Object).WithField(VisitFieldWithArguments(node)),
        { Method: var method } when method.IsOperation() => this.expression.WithField(VisitFieldWithArguments(node)),
        { Arguments: var args } when args.Any(x => x.UnwrapLambdaFromQuote() is not null) => this.VisitLambdas(node),
        { Method.Name: nameof(Enumerable.OfType) } => this.TraverseExtensionMethodCall(node).WithField(new SpreadExpression(node.Method.GetGenericArguments()[0])),
        _ => base.VisitMethodCall(node),
    };

    /// <inheritdoc/>
    protected override Expression VisitUnary(UnaryExpression node) => node switch
    {
        { NodeType: ExpressionType.TypeAs } => this.TraverseMember(node.Operand),
        { NodeType: ExpressionType.Convert } => this.TraverseMember(node.Operand),
        _ => base.VisitUnary(node),
    };

    private Expression VisitLambdas(MethodCallExpression methodCallExpression)
    {
        var parent = methodCallExpression.Object is not null
            ? this.TraverseMember(methodCallExpression.Object)
            : methodCallExpression.Method.IsDefined(typeof(ExtensionAttribute))
                ? this.Visit(methodCallExpression.Arguments[0]) as TypeFieldExpression
                : this.expression;

        var translator = new ExpressionTranslator(parent ?? throw new InvalidOperationException("Not a TypeFieldExpression"));

        var lamdas = methodCallExpression.Arguments
            .Select(x => x.UnwrapLambdaFromQuote())
            .Where(x => x is not null)
            .Select(translator.Visit)
            .ToList();

        return methodCallExpression;
    }

    private TypeFieldExpression TraverseMember(Expression member)
    {
        var innerExpression = this.Visit(member) as TypeFieldExpression;

        return innerExpression ?? this.expression;
    }

    private TypeFieldExpression TraverseExtensionMethodCall(MethodCallExpression member)
    {
        var innerExpression = base.VisitMethodCall(member) as MethodCallExpression;

        return innerExpression?.Arguments[0] as TypeFieldExpression ?? this.expression;
    }

    private static TypeFieldExpression VisitFieldWithArguments(MethodCallExpression node)
    {
        var field = (TypeFieldExpression)node.Method.ToField();

        return node.Method.GetParameters()
            .Zip(node.Arguments, (p, i) => (Name: p.Name!, Value: ArgumentVistor.GetValue(i)))
            .Aggregate(field, (f, arg) => field.WithArgument(arg.Name, arg.Value));
    }
}
