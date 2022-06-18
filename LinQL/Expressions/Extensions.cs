namespace LinQL.Expressions;

using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using LinQL.Description;

internal static class Extensions
{
    public static TIn GetOrAdd<T, TIn>(this IDictionary<string, T> that, string key, Func<TIn> creator)
        where TIn : T
    {
        if (that.TryGetValue(key, out var value))
        {
            return (value is TIn @in) ? @in : throw new InvalidOperationException("Key already contains a value of a different type");
        }

        var newValue = creator();

        that.Add(key, newValue);

        return newValue;
    }

    public static string ToCamelCase(this string that) => JsonNamingPolicy.CamelCase.ConvertName(that);

    public static bool IsScalar(this Type type) => type.IsPrimitive || type.IsEnum || type.Equals(typeof(string));

    public static FieldExpression ToField(this MemberInfo member) => member switch
    {
        PropertyInfo prop when prop.PropertyType.IsScalar() => new ScalarFieldExpression(member.GetFieldName(), prop.PropertyType),
        FieldInfo field when field.FieldType.IsScalar() => new ScalarFieldExpression(member.GetFieldName(), field.FieldType),
        MethodInfo method when method.IsOperation() => new TypeFieldExpression(member.GetFieldName(), method.ReturnType),
        PropertyInfo prop => new TypeFieldExpression(prop.GetFieldName(), prop.PropertyType),
        FieldInfo field => new TypeFieldExpression(field.GetFieldName(), field.FieldType),
        _ => throw new NotSupportedException(),
    };

    public static string GetFieldName(this MemberInfo member)
        => member.GetCustomAttribute<GraphQLFieldAttribute>()?.Name ?? member.Name.ToCamelCase();

    public static bool IsOperation(this MethodInfo method) => method.GetCustomAttribute<GraphQLOperationAttribute>() is not null;

    public static LambdaExpression? UnwrapLambdaFromQuote(this Expression expression)
        => (expression is UnaryExpression unary && expression.NodeType == ExpressionType.Quote
            ? unary.Operand
            : expression) as LambdaExpression;
}
