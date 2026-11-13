namespace LinQL.Filters;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, string Name, JsonIgnoreCondition? Ignore)[]> PropertyCache = new();

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
    /// <exception cref="InvalidOperationException">
    /// The filter input object has a non-nullable value-type property, so an operation that was never set
    /// cannot be told apart from its default value.
    /// </exception>
    public static FilterValue From(object? filter)
    {
        var value = Create();

        if (Normalize(filter, strict: true) is IReadOnlyDictionary<string, object?> normalized)
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
            .Select(field => (field.Key, Value: Normalize(field.Value, strict: false)))
            .Where(field => field.Value is not null)
            .ToDictionary(field => field.Key, field => field.Value);

    /// <summary>
    /// Converts a value to the shape GraphQL variables take: camelCased field names, enums by name,
    /// and nulls dropped.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <returns>The value as GraphQL variables expect it.</returns>
    internal static object? Normalize(object? value) => Normalize(value, strict: false);

    private static object? Normalize(object? value, bool strict) => value switch
    {
        null => null,
        FilterValue filter => filter.ToDictionary() is { Count: > 0 } dictionary ? dictionary : null,
        string text => text,
        Enum enumValue => enumValue.ToString(),
        // Already wire-shaped: serialising one of these writes its own JSON, which reflecting over it
        // would replace with its properties (`valueKind`, `rawText`, …).
        JsonElement element => element,
        JsonDocument document => document.RootElement,
        object scalar when IsScalar(scalar.GetType()) => scalar,
        object dictionary when TryGetEntries(dictionary, out var entries) => NormalizeDictionary(entries!, strict),
        IEnumerable enumerable => NormalizeEnumerable(enumerable, strict),
        object other => NormalizeObject(other, strict),
    };

    private static Dictionary<string, object?>? NormalizeDictionary(IEnumerable<KeyValuePair<string, object?>> dictionary, bool strict)
    {
        var normalized = new Dictionary<string, object?>();

        foreach (var field in dictionary)
        {
            if (Normalize(field.Value, strict) is { } value)
            {
                normalized[field.Key] = value;
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static object? NormalizeEnumerable(IEnumerable enumerable, bool strict)
    {
        var items = enumerable.Cast<object?>().Select(item => Normalize(item, strict)).Where(item => item is not null).ToList();

        return items.Count == 0 ? null : items;
    }

    private static Dictionary<string, object?>? NormalizeObject(object value, bool strict)
    {
        var normalized = new Dictionary<string, object?>();

        foreach (var (property, name, ignore) in PropertyCache.GetOrAdd(
            value.GetType(),
            static type =>
            [
                .. type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => (
                        Property: property,
                        Name: property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name.ToCamelCase(),
                        Ignore: property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition)),
            ]))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var raw = property.GetValue(value);

            if (IsIgnored(ignore, property.PropertyType, raw))
            {
                continue;
            }

            // A filter operation expresses "not set" as null, so a value type that cannot hold null has no
            // way to say it: sending the default would silently filter on 0 or false. Arguments are not
            // strict about this, because a value the caller passed is always meaningful to them.
            if (strict && property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null)
            {
                throw new InvalidOperationException(
                    $"'{value.GetType().Name}.{property.Name}' is a non-nullable {property.PropertyType.Name}, so an operation that was never set cannot be told apart from its default value. Declare it as '{property.PropertyType.Name}?' on a filter input type.");
            }

            if (Normalize(raw, strict) is { } fieldValue)
            {
                normalized[name] = fieldValue;
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    /// <summary>
    /// Gets whether a property is left out of the payload, either always or because its value counts as
    /// unset for the condition it declares.
    /// </summary>
    /// <param name="condition">The declared <see cref="JsonIgnoreAttribute.Condition"/>.</param>
    /// <param name="type">The property type.</param>
    /// <param name="value">The property value.</param>
    /// <returns>Whether the property is left out.</returns>
    private static bool IsIgnored(JsonIgnoreCondition? condition, Type type, object? value) => condition switch
    {
        JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenWriting => true,
        JsonIgnoreCondition.WhenWritingNull => value is null,
        JsonIgnoreCondition.WhenWritingDefault => value is null || (type.IsValueType && value.Equals(Activator.CreateInstance(type))),
        JsonIgnoreCondition.Never or JsonIgnoreCondition.WhenReading => false,

        // null and any unnamed value keep the property.
        _ => false,
    };

    /// <summary>
    /// Gets the entries of a dictionary, so one is never mistaken for a sequence.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="entries">The entries, keyed by the name each one goes out under.</param>
    /// <returns>Whether the value is a dictionary.</returns>
    /// <remarks>
    /// A dictionary is usually typed more tightly than <c>Dictionary&lt;string, object?&gt;</c>
    /// (<c>Dictionary&lt;string, string&gt;</c> is the common case), and the non-generic implementations
    /// such as <see cref="Hashtable"/> match no generic arm at all. Without this they would reach the
    /// enumerable arm and serialise as <c>[{{"key": …}}]</c> instead of a JSON object.
    /// </remarks>
    private static bool TryGetEntries(object value, out IEnumerable<KeyValuePair<string, object?>>? entries)
    {
        switch (value)
        {
            case IDictionary dictionary:
                entries = Entries(dictionary);
                return true;

            case IEnumerable enumerable when ReadOnlyDictionaryValueType(value.GetType()) is { } valueType:
                var pair = typeof(KeyValuePair<,>).MakeGenericType(typeof(string), valueType);
                var key = pair.GetProperty("Key");
                var item = pair.GetProperty("Value");

                entries = enumerable
                    .Cast<object>()
                    .Select(entry => new KeyValuePair<string, object?>(NameOf(key?.GetValue(entry)), item?.GetValue(entry)));
                return true;

            default:
                entries = null;
                return false;
        }
    }

    /// <summary>
    /// Reads the entries of a dictionary through <see cref="IDictionary.GetEnumerator"/>.
    /// </summary>
    /// <remarks>
    /// Casting the dictionary to <c>IEnumerable&lt;DictionaryEntry&gt;</c> does not work: a generic
    /// dictionary enumerates as <c>KeyValuePair&lt;,&gt;</c> through <see cref="IEnumerable"/>, even when
    /// it is typed as <see cref="IDictionary"/>, so only the dictionary's own enumerator yields entries.
    /// </remarks>
    private static IEnumerable<KeyValuePair<string, object?>> Entries(IDictionary dictionary)
    {
        var enumerator = dictionary.GetEnumerator();

        while (enumerator.MoveNext())
        {
            yield return new KeyValuePair<string, object?>(NameOf(enumerator.Key), enumerator.Value);
        }
    }

    private static Type? ReadOnlyDictionaryValueType(Type type)
        => type.GetInterfaces()
            .Where(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) && x.GetGenericArguments()[0] == typeof(string))
            .Select(x => x.GetGenericArguments()[1])
            .FirstOrDefault();

    private static string NameOf(object? key)
        => Convert.ToString(key, CultureInfo.InvariantCulture) ?? string.Empty;

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
