namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;

internal class ArgumentVistor : ExpressionVisitor
{
    private object? value;

    private ArgumentVistor() { }

    public static object? GetValue(Expression expression)
    {
        var visitor = new ArgumentVistor();
        visitor.Visit(expression);
        return visitor.value;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        this.value = node.Value;

        return base.VisitConstant(node);
    }

    protected override Expression VisitNew(NewExpression node)
    {
        this.value = node.Constructor!.Invoke(node.Arguments.Select(GetValue).ToArray());

        return base.VisitNew(node);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.Expression?.NodeType == ExpressionType.Constant)
        {
            var instance = (node.Expression as ConstantExpression)?.Value;

            var value = node.Member switch
            {
                PropertyInfo prop => prop.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => throw new NotSupportedException(),
            };

            return this.VisitConstant(Expression.Constant(value, node.Type));
        }

        var objectMember = Expression.Convert(node, typeof(object));

        var getter = Expression.Lambda<Func<object>>(objectMember);

        this.value = getter.CompileFast()();

        return node;
    }
}
