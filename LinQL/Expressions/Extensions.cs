namespace LinQL.Expressions;

using System;
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

    public static bool IsScalar(this Type type, IEnumerable<Scalar> scalars)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsEnum || scalars.Any(s => s.RuntimeType == type.FullName || ((type.IsPrimitive || type.Equals(typeof(string))) && s.OriginalPrimitive == type.FullName));
    }

    public static bool IsArrayOfScalars(this Type type, IEnumerable<Scalar> scalars)
    {
        if (!type.IsArray)
        {
            return false;
        }

        return type.GetElementType()?.IsScalar(scalars) ?? false;
    }

    public static FieldExpression ToField(this MemberInfo member, IRootExpression root) => member switch
    {
        PropertyInfo prop when prop.PropertyType.IsScalar(root.Scalars) || prop.PropertyType.IsArrayOfScalars(root.Scalars) => new ScalarFieldExpression(member.GetFieldName(), prop.PropertyType, member.DeclaringType!, root),
        FieldInfo field when field.FieldType.IsScalar(root.Scalars) || field.FieldType.IsArrayOfScalars(root.Scalars) => new ScalarFieldExpression(member.GetFieldName(), field.FieldType, member.DeclaringType!, root),
        MethodInfo method when (method.ReturnType.IsArrayOfScalars(root.Scalars) || method.ReturnType.IsScalar(root.Scalars)) && !method.GetParameters().Any() => new ScalarFieldExpression(method.GetFieldName(), method.ReturnType, member.DeclaringType!, root),
        MethodInfo method when (method.ReturnType.IsArrayOfScalars(root.Scalars) || method.ReturnType.IsScalar(root.Scalars)) && method.IsOperation() => new ScalarFieldExpression(member.GetFieldName(), method.ReturnType, member.DeclaringType!, root),
        MethodInfo method when method.IsOperation() => new TypeFieldExpression(member.GetFieldName(), method.ReturnType, member.DeclaringType!, root),
        PropertyInfo prop => new TypeFieldExpression(prop.GetFieldName(), prop.PropertyType, member.DeclaringType!, root),
        FieldInfo field => new TypeFieldExpression(field.GetFieldName(), field.FieldType, member.DeclaringType!, root),
        _ => throw new NotSupportedException(),
    };

    public static string GetTypeName(this Type member)
        => member.GetCustomAttribute<GraphQLFieldAttribute>()?.Name ?? member.Name.ToCamelCase();

    public static string GetFieldName(this MemberInfo member)
        => member.GetCustomAttribute<GraphQLFieldAttribute>()?.Name ?? member.Name.ToCamelCase();

    public static bool IsOperation(this MethodInfo method) => method.GetCustomAttribute<GraphQLOperationAttribute>() is not null;

    public static bool IsRootOperation(this Type type) => type.GetCustomAttribute<OperationTypeAttribute>() is not null;

    public static LambdaExpression? UnwrapLambdaFromQuote(this Expression expression)
        => (expression is UnaryExpression unary && expression.NodeType == ExpressionType.Quote
            ? unary.Operand
            : expression) as LambdaExpression;

    public static bool IsOn(this MethodCallExpression method)
        => method.Method.DeclaringType?.Equals(typeof(SelectExtentions)) == true
            && method.Method.Name == nameof(SelectExtentions.On);

    public static IEnumerable<FieldExpression> GetAllScalars(this Type type, IRootExpression root)
        => type.GetProperties().Where(p => p.PropertyType.IsScalar(root.Scalars)).Select(x => x.ToField(root));

    public static bool IsAssignableFromGenericInterface(this Type type, Type genericInterface)
        => type.GetInterfaces().Any(@interface => @interface.IsAssignableFrom(genericInterface));

    public static string GetArgumentType(this ParameterInfo argument)
        => argument.GetCustomAttribute<GraphQLArgumentAttribute>().GQLType;


    private static readonly Type[] CollectionTypes = [typeof(IEnumerable<>), typeof(List<>), typeof(ICollection<>), typeof(IReadOnlyCollection<>)];
    public static bool RequiresTypeName(this Type type)
    {
        if (type.IsGenericType && CollectionTypes.Contains(type.GetGenericTypeDefinition()))
        {
            return type.GetGenericArguments().First().IsInterface;
        }

        if (type.IsArray && type.GetElementType().IsInterface)
        {
            return true;
        }

        if (type.IsInterface)
        {
            return true;
        }

        return false;
    }
}
