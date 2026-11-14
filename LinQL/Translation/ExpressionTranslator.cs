namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using LinQL.Description;
using LinQL.Expressions;
using LinQL.Filters;

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
    /// <param name="options">The current configuration.</param>
    /// <returns>The translated expression.</returns>
    /// <exception cref="InvalidOperationException">No <see cref="OperationTypeAttribute"/> is defined on <typeparamref name="TRoot"/></exception>
    public static GraphQLExpression<TRoot, TData> Translate<TRoot, TData>(Expression<Func<TRoot, TData>> query, LinQLOptions options)
        where TRoot : RootType<TRoot>
    {
        var operationType = typeof(TRoot).GetCustomAttribute<OperationTypeAttribute>()?.Operation
            ?? throw new InvalidOperationException($"{typeof(TRoot)} does not have an {nameof(OperationTypeAttribute)} defined");

        var root = new GraphQLExpression<TRoot, TData>(operationType, query, options);
        var translator = new ExpressionTranslator(root);
        translator.Visit(query.Body);
        return root;
    }

    /// <inheritdoc/>
    protected override Expression VisitMember(MemberExpression node) => node switch
    {
        { Member: FieldInfo field, Expression: object } => this.TraverseMember(node.Expression).WithField(field.ToField(this.expression.Root)),
        { Member: FieldInfo field } => this.expression.WithField(field.ToField(this.expression.Root)),
        { Member: PropertyInfo property, Expression: object } => this.TraverseMember(node.Expression).WithField(property.ToField(this.expression.Root)),
        { Member: PropertyInfo property } => this.expression.WithField(property.ToField(this.expression.Root)),
        _ => base.VisitMember(node),
    };

    /// <inheritdoc/>
    protected override Expression VisitMethodCall(MethodCallExpression node) => node switch
    {
        var m when m.IsOn() => this.TraverseOn(node),
        { Method: var method } when method.IsOperation() && method.ReturnType.IsScalar(this.expression.Root.Scalars) && !method.GetParameters().Any() => this.expression.WithField(node.Method.ToField(this.expression.Root)),
        { Object: object, Method: var method } when method.IsOperation() => this.TraverseMember(node.Object).WithField(VisitFieldWithArguments(node, this.expression.Root)),
        { Method: var method } when method.IsOperation() => this.expression.WithField(VisitFieldWithArguments(node, this.expression.Root)),
        { Method.Name: nameof(Enumerable.OfType) } => this.TraverseExtensionMethodCall(node).WithField(new SpreadExpression(node.Method.GetGenericArguments()[0], this.expression.Root)),
        { Method.Name: nameof(Enumerable.Select) } => this.TraverseSelect(node),
        { Method.Name: nameof(SelectExtentions.SelectAll), Method.DeclaringType.Name: nameof(SelectExtentions) } => this.TraverseSelectAll(node),
        { Method: var method } when method.GetCustomAttribute<GraphQLFilterAttribute>() is not null => this.TraverseFilter(node),
        { Method: var method } when method.GetCustomAttribute<GraphQLArgumentAttribute>() is not null && method.IsDefined(typeof(ExtensionAttribute)) => this.TraverseArgument(node),
        { Method: var method } when method.GetCustomAttribute<GraphQLIncludesAttribute>() is not null => this.TraverseIncludes(node),
        { Arguments: var args } when args.Any(x => x.UnwrapLambdaFromQuote() is not null) => this.VisitLambdas(node),
        _ => base.VisitMethodCall(node),
    };

    /// <inheritdoc/>
    protected override Expression VisitUnary(UnaryExpression node) => node switch
    {
        // A conversion to object is the lambda's return coercion — an Expression<Func<…, object>> body
        // always carries one — rather than a cast the query should see, so it is not a spread.
        { NodeType: ExpressionType.Convert, Type: var type, Operand: var operand } when type == typeof(object) => this.Visit(operand),
        { NodeType: ExpressionType.TypeAs or ExpressionType.Convert } t when !t.Type.Equals(this.expression.Type)
            => this.TraverseMember(node.Operand).WithField(new SpreadExpression(node.Type, this.expression.Root)),
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

        // The body is visited rather than the quoted lambda. A visit returns the field it added, which is
        // not the lambda's return type, and ExpressionVisitor rebuilds a lambda from its visited body.
        var selection = member.Arguments[1].UnwrapLambdaFromQuote()
            ?? throw new InvalidOperationException($"'{member.Method.Name}' must be given a lambda.");
        translator.Visit(selection.Body);

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

        foreach (var scalar in type.GetAllScalars(this.expression.Root))
        {
            field.WithField(scalar);
        }

        return field.ReplaceType(member.Method.ReturnType);
    }

    private Expression TraverseOn(MethodCallExpression member)
    {
        var field = new SpreadExpression(member.Method.GetGenericArguments()[1], this.expression.Root);
        var translator = new ExpressionTranslator(field);

        // See TraverseSelect: visiting the quoted lambda would rebuild it with a field as its body.
        var spread = member.Arguments[1].UnwrapLambdaFromQuote()
            ?? throw new InvalidOperationException($"'{member.Method.Name}' must be given a lambda.");
        translator.Visit(spread.Body);

        var parent = member.Arguments[0] is MethodCallExpression m && m.IsOn()
            ? this.TraverseChainedOn(m)
            : this.TraverseMember(member.Arguments[0]);

        parent.WithField(field);

        // The spread is returned rather than the call, so anything chained after On attaches to the
        // ... on block. This matches OfType, which already resolves to its spread.
        return field;
    }

    private TypeFieldExpression TraverseChainedOn(MethodCallExpression member)
    {
        var field = new SpreadExpression(member.Method.GetGenericArguments()[1], this.expression.Root);
        var translator = new ExpressionTranslator(field);
        translator.Visit(member.Arguments[1].UnwrapLambdaFromQuote()?.Body ?? member.Arguments[1]);

        var parent = member.Arguments[0] is MethodCallExpression m && m.IsOn()
            ? this.TraverseChainedOn(m)
            : this.TraverseMember(member.Arguments[0]);

        parent.WithField(field);

        return parent;
    }

    private Expression TraverseFilter(MethodCallExpression node)
    {
        var filter = node.Method.GetCustomAttribute<GraphQLFilterAttribute>()
            ?? throw new InvalidOperationException($"'{node.Method.Name}' is not a filter extension.");

        var field = this.TraverseField(node);

        var value = FilterExpressionTranslator.Translate(node.Arguments[1]);

        if (value.IsEmpty)
        {
            throw new InvalidOperationException($"The filter on '{field.FieldName}' has no operations. Omit the filter to query without one.");
        }

        field.WithArgument(filter.Argument, filter.GQLType, value.ToDictionary());

        return field;
    }

    /// <summary>
    /// Adds the fields the include lambda selects to the selection the method is called on.
    /// </summary>
    /// <remarks>
    /// The lambda is translated with the receiver's field expression as its root, which is what lets an
    /// include target a nested selection instead of only the document root. Inside a <c>Select</c> or an
    /// <c>On</c> lambda the receiver is that lambda's parameter, and the include joins the selection the
    /// lambda is scoped to.
    /// </remarks>
    private Expression TraverseIncludes(MethodCallExpression node)
    {
        var field = this.TraverseIncludeTarget(node);

        var include = node.Arguments[1].UnwrapLambdaFromQuote()
            ?? throw new InvalidOperationException($"'{node.Method.Name}' must be given a lambda.");

        var translator = new ExpressionTranslator(field);
        translator.Visit(include.Body);

        return field;
    }

    /// <summary>
    /// Gets the selection an include adds to: the field the method is called on, or, when it is called on
    /// a lambda's parameter, the selection that lambda is scoped to.
    /// </summary>
    private TypeFieldExpression TraverseIncludeTarget(MethodCallExpression include)
    {
        if (include.Arguments[0] is ParameterExpression)
        {
            return this.expression;
        }

        return this.Visit(include.Arguments[0]) as TypeFieldExpression
            ?? throw new InvalidOperationException($"'{include.Method.Name}' must be called on a field or on the parameter of the selection it grows.");
    }

    private Expression TraverseArgument(MethodCallExpression node)
    {
        var argument = node.Method.GetCustomAttribute<GraphQLArgumentAttribute>()
            ?? throw new InvalidOperationException($"'{node.Method.Name}' is not a field argument extension.");

        var field = this.TraverseField(node);

        // A value the caller passed is meaningful as given, so this is the lenient normalisation:
        // input objects reach the wire in camelCase with enums by name rather than in CLR shape.
        field.WithArgument(
            argument.Name ?? (char.ToLowerInvariant(node.Method.Name[0]) + node.Method.Name.Substring(1)),
            argument.GQLType,
            FilterValue.Normalize(ArgumentVistor.GetValue(node.Arguments[1])));

        return field;
    }

    /// <summary>
    /// Gets the field an extension method is extending: its first argument.
    /// </summary>
    private TypeFieldExpression TraverseField(MethodCallExpression extension)
        => this.Visit(extension.Arguments[0]) as TypeFieldExpression
            ?? throw new InvalidOperationException($"'{extension.Method.Name}' must be called on a field.");

    private static FieldExpression VisitFieldWithArguments(MethodCallExpression node, IRootExpression root)
    {
        var field = node.Method.ToField(root);

        return node.Method.GetParameters()
            .Zip(node.Arguments, (p, i) => (
                p.Name,
                Type: p.GetArgumentType(),
                Value: ArgumentVistor.GetValue(i)))
            .Aggregate(field, (f, arg) => field.WithArgument(arg.Name, arg.Type, arg.Value));
    }
}
