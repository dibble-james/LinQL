namespace LinQL.Expressions;

using System.Collections.ObjectModel;
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

    public static bool IsScalar(this Type type) => type.IsPrimitive || type.IsEnum || type.Equals(typeof(string));

    public static FieldExpression ToField(this MemberInfo member, IRootExpression root) => member switch
    {
        PropertyInfo prop when prop.PropertyType.IsScalar() => new ScalarFieldExpression(member.GetFieldName(), prop.PropertyType, member.DeclaringType!),
        FieldInfo field when field.FieldType.IsScalar() => new ScalarFieldExpression(member.GetFieldName(), field.FieldType, member.DeclaringType!),
        MethodInfo method when method.ReturnType.IsScalar() && !method.GetParameters().Any() => new ScalarFieldExpression(method.GetFieldName(), method.ReturnType, member.DeclaringType!),
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
        => type.GetProperties().Where(p => p.PropertyType.IsScalar()).Select(x => x.ToField(root));

    public static bool IsAssignableFromGenericInterface(this Type type, Type genericInterface)
        => type.GetInterfaces().Any(@interface => @interface.IsAssignableFrom(genericInterface));

    public static (Type Type, bool Nullable) IsParameterNullable(this ParameterInfo parameter)
    {
        var memberType = parameter.ParameterType;
        var declaringType = parameter.Member;
        var customAttributes = parameter.CustomAttributes;

        if (memberType.IsValueType)
        {
            var nullableType = Nullable.GetUnderlyingType(memberType);

            return (nullableType ?? memberType, nullableType is not null);
        }

        var nullable = customAttributes
            .FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
        if (nullable != null && nullable.ConstructorArguments.Count == 1)
        {
            var attributeArgument = nullable.ConstructorArguments[0];
            if (attributeArgument.ArgumentType == typeof(byte[]))
            {
                var args = (ReadOnlyCollection<CustomAttributeTypedArgument>)attributeArgument.Value!;
                if (args.Count > 0 && args[0].ArgumentType == typeof(byte))
                {
                    return (memberType, (byte)args[0].Value! == 2);
                }
            }
            else if (attributeArgument.ArgumentType == typeof(byte))
            {
                return (memberType, (byte)attributeArgument.Value! == 2);
            }
        }

        for (var type = declaringType; type != null; type = type.DeclaringType)
        {
            var context = type.CustomAttributes
                .FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");
            if (context != null &&
                context.ConstructorArguments.Count == 1 &&
                context.ConstructorArguments[0].ArgumentType == typeof(byte))
            {
                return (memberType, (byte)context.ConstructorArguments[0].Value! == 2);
            }
        }

        // Couldn't find a suitable attribute
        return (memberType, false);
    }
}
