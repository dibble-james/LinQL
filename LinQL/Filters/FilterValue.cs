namespace LinQL.Filters;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LinQL.Expressions;

/// <summary>
/// The value of a Hot Chocolate filter argument.
/// </summary>
/// <remarks>
/// The value is built as the nested dictionaries the wire format expects, and only values that were
/// explicitly set are emitted: nulls and empty nested filters are dropped. This matters because Hot
/// Chocolate treats an explicit <c>null</c> operation as a value (on a nullable property it means
/// <c>IS NULL</c>), and an empty filter object is either an error or a silent no-op depending on the
/// server version.
/// </remarks>
public sealed class FilterValue
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    private readonly Dictionary<string, object?> fields = [];

    /// <summary>
    /// Gets a value indicating whether no operation has been set.
    /// </summary>
    public bool IsEmpty => this.ToDictionary().Count == 0;

    /// <summary>
    /// Creates an empty filter value.
    /// </summary>
    /// <returns>The empty <see cref="FilterValue"/>.</returns>
    public static FilterValue Create() => new();

    /// <summary>
    /// Creates a filter value from a generated filter input object.
    /// </summary>
    /// <param name="filter">The filter input object.</param>
    /// <returns>The equivalent <see cref="FilterValue"/>.</returns>
    public static FilterValue From(object? filter)
    {
        var value = Create();

        if (Normalize(filter) is IReadOnlyDictionary<string, object?> normalized)
        {
            foreach (var field in normalized)
            {
                value.fields[field.Key] = field.Value;
            }
        }

        return value;
    }

    /// <summary>
    /// Sets an operation or a nested filter.
    /// </summary>
    /// <param name="name">The GraphQL field name.</param>
    /// <param name="value">The operation value or nested filter.</param>
    /// <returns>The updated <see cref="FilterValue"/>.</returns>
    public FilterValue Set(string name, object? value)
    {
        this.fields[name] = value;

        return this;
    }

    /// <summary>
    /// Appends a sub-filter to a combinator such as <c>and</c> or <c>or</c>.
    /// </summary>
    /// <param name="name">The GraphQL field name of the combinator.</param>
    /// <param name="value">The sub-filter.</param>
    /// <returns>The updated <see cref="FilterValue"/>.</returns>
    public FilterValue Append(string name, FilterValue value)
    {
        if (!this.fields.TryGetValue(name, out var existing) || existing is not List<object?> list)
        {
            list = [];
            this.fields[name] = list;
        }

        list.Add(value);

        return this;
    }

    /// <summary>
    /// Adds a sub-filter to a combinator, folding everything set so far into the first branch.
    /// </summary>
    /// <param name="name">The GraphQL field name of the combinator.</param>
    /// <param name="value">The new sub-filter.</param>
    /// <returns>The updated <see cref="FilterValue"/>.</returns>
    /// <remarks>
    /// <c>and</c> and <c>or</c> take a list of filters. Everything built before the combinator is a sibling
    /// field of it, so it has to become a branch of its own: <c>price.gt.Or(name.eq)</c> is
    /// <c>or: [{ price: { gt: 10 } }, { name: { eq: "x" } }]</c>, not a sibling <c>price</c> next to an
    /// <c>or</c> list, which Hot Chocolate would read as <c>and</c>.
    /// </remarks>
    public FilterValue Combine(string name, FilterValue value)
    {
        if (this.fields.ContainsKey(name))
        {
            return this.Append(name, value);
        }

        var branch = Create();

        foreach (var field in this.fields)
        {
            branch.fields[field.Key] = field.Value;
        }

        this.fields.Clear();

        return this.Append(name, branch).Append(name, value);
    }

    /// <summary>
    /// Gets the filter as nested dictionaries, excluding values that were never set.
    /// </summary>
    /// <returns>The filter value.</returns>
    public IReadOnlyDictionary<string, object?> ToDictionary()
        => this.fields
            .Select(field => (field.Key, Value: Normalize(field.Value)))
            .Where(field => field.Value is not null)
            .ToDictionary(field => field.Key, field => field.Value);

    /// <summary>
    /// Converts a value to the shape GraphQL variables take: camelCased field names, enums by name,
    /// and nulls dropped.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value as GraphQL variables expect it.</returns>
    internal static object? Normalize(object? value) => value switch
    {
        null => null,
        FilterValue filter => filter.ToDictionary() is { Count: > 0 } dictionary ? dictionary : null,
        string text => text,
        Enum enumValue => enumValue.ToString(),
        object scalar when IsScalar(scalar.GetType()) => scalar,
        IDictionary<string, object?> dictionary => NormalizeDictionary(dictionary),
        IReadOnlyDictionary<string, object?> readOnlyDictionary => NormalizeDictionary(readOnlyDictionary),
        IEnumerable enumerable => NormalizeEnumerable(enumerable),
        object other => NormalizeObject(other),
    };

    private static Dictionary<string, object?>? NormalizeDictionary(IEnumerable<KeyValuePair<string, object?>> dictionary)
    {
        var normalized = new Dictionary<string, object?>();

        foreach (var field in dictionary)
        {
            if (Normalize(field.Value) is { } value)
            {
                normalized[field.Key] = value;
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static object? NormalizeEnumerable(IEnumerable enumerable)
    {
        var items = enumerable.Cast<object?>().Select(Normalize).Where(item => item is not null).ToList();

        return items.Count == 0 ? null : items;
    }

    private static Dictionary<string, object?>? NormalizeObject(object value)
    {
        var normalized = new Dictionary<string, object?>();

        foreach (var property in PropertyCache.GetOrAdd(value.GetType(), static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (Normalize(property.GetValue(value)) is { } fieldValue)
            {
                normalized[property.Name.ToCamelCase()] = fieldValue;
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsPrimitive
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan);
    }
}
