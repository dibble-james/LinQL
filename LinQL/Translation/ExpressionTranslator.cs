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
    /// <param name="query">The query to translate.</param>
    /// <returns>The translated expression.</returns>
    /// <exception cref="InvalidOperationException">No <see cref="OperationTypeAttribute"/> is defined on <typeparamref name="TRoot"/></exception>
    public static GraphQLExpression<TRoot, TData> Translate<TRoot, TData>(Expression<Func<TRoot, TData>> query)
    {
        var operationType = typeof(TRoot).GetCustomAttribute<OperationTypeAttribute>()?.Operation
            ?? throw new InvalidOperationException($"{typeof(TRoot)} does not have an {nameof(OperationTypeAttribute)} defined");

        var root = new GraphQLExpression<TRoot, TData>(operationType, query);
        var translator = new ExpressionTranslator(root);
        translator.Visit(query.Body);
        return root;
    }

    /// <summary>
    /// Add an extra field to the selection.
    /// </summary>
    /// <typeparam name="TRoot">The root operation to start the query from.</typeparam>
    /// <typeparam name="TData">The result of the query.</typeparam>
    /// <param name="expression">The original expression.</param>
    /// <param name="include">The field to include.</param>
    /// <returns>The translated expression.</returns>
    public static GraphQLExpression<TRoot, TData> Include<TRoot, TData>(GraphQLExpression<TRoot, TData> expression, Expression<Func<TRoot, object>> include)
    {
        var translator = new ExpressionTranslator(expression);
        translator.Visit(include.Body);
        return expression;
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
        var m when m.IsOn() => this.TraverseOn(node),
        { Object: object, Method: var method } when method.IsOperation() => this.TraverseMember(node.Object).WithField(VisitFieldWithArguments(node)),
        { Method: var method } when method.IsOperation() => this.expression.WithField(VisitFieldWithArguments(node)),
        { Method.Name: nameof(Enumerable.OfType) } => this.TraverseExtensionMethodCall(node).WithField(new SpreadExpression(node.Method.GetGenericArguments()[0])),
        { Method.Name: nameof(Enumerable.Select) } => this.TraverseSelect(node),
        { Method.Name: nameof(SelectExtentions.SelectAll), Method.DeclaringType.Name: nameof(SelectExtentions) } => this.TraverseSelectAll(node),
        { Arguments: var args } when args.Any(x => x.UnwrapLambdaFromQuote() is not null) => this.VisitLambdas(node),
        _ => base.VisitMethodCall(node),
    };

    /// <inheritdoc/>
    protected override Expression VisitUnary(UnaryExpression node) => node switch
    {
        { NodeType: ExpressionType.TypeAs or ExpressionType.Convert } t when !t.Type.Equals(this.expression.Type)
            => this.TraverseMember(node.Operand).WithField(new SpreadExpression(node.Type)),
        { NodeType: ExpressionType.Convert, Operand: MethodCallExpression }
            => node.Update(this.Visit(node.Operand)),
        _ => base.VisitUnary(node),
    };

    private Expression VisitLambdas(MethodCallExpression methodCallExpression)
    {
        if (methodCallExpression.IsOn())
        {
            return this.TraverseOn(methodCallExpression);
        }

        var parent = methodCallExpression switch
        {
            { Object: not null } => this.TraverseMember(methodCallExpression.Object),
            { Method: var m } when m.IsDefined(typeof(ExtensionAttribute)) => this.TraverseMember(methodCallExpression.Arguments[0]),
            _ => this.expression,
        };

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

    private TypeFieldExpression TraverseSelect(MethodCallExpression member)
    {
        var field = this.TraverseMember(member.Arguments[0]);
        var translator = new ExpressionTranslator(field);
        translator.Visit(member.Arguments[1]);

        return field.ReplaceType(member.Method.ReturnType);
    }

    private TypeFieldExpression TraverseSelectAll(MethodCallExpression member)
    {
        var field = this.TraverseMember(member.Arguments[0]);

        var type =
            field.Type.IsArray
            ? field.Type.GetElementType() :
            typeof(IEnumerable<>).IsAssignableFromGenericInterface(field.Type)
            ? field.Type.GetGenericArguments()[0]
            : field.Type;

        foreach (var scalar in type.GetAllScalars())
        {
            field.WithField(scalar);
        }

        return field.ReplaceType(member.Method.ReturnType);
    }

    private Expression TraverseOn(MethodCallExpression member)
    {
        var field = new SpreadExpression(member.Method.GetGenericArguments()[1]);
        var translator = new ExpressionTranslator(field);
        translator.Visit(member.Arguments[1]);

        var parent = member.Arguments[0] is MethodCallExpression m && m.IsOn()
            ? this.TraverseChainedOn(m)
            : this.TraverseMember(member.Arguments[0]);

        parent.WithField(field);

        return member;
    }

    private TypeFieldExpression TraverseChainedOn(MethodCallExpression member)
    {
        var field = new SpreadExpression(member.Method.GetGenericArguments()[1]);
        var translator = new ExpressionTranslator(field);
        translator.Visit(member.Arguments[1]);

        var parent = member.Arguments[0] is MethodCallExpression m && m.IsOn()
            ? this.TraverseChainedOn(m)
            : this.TraverseMember(member.Arguments[0]);

        parent.WithField(field);

        return parent;
    }

    private static TypeFieldExpression VisitFieldWithArguments(MethodCallExpression node)
    {
        var field = (TypeFieldExpression)node.Method.ToField();

        return node.Method.GetParameters()
            .Zip(node.Arguments, (p, i) => (Name: p.Name!, Value: ArgumentVistor.GetValue(i)))
            .Aggregate(field, (f, arg) => field.WithArgument(arg.Name, arg.Value));
    }
}
