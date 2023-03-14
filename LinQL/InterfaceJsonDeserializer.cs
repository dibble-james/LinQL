namespace LinQL;

using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinQL.Description;

/// <summary>
/// A JSON converter that respects the __typename from the server.
/// </summary>
/// <typeparam name="T">The interface to deserialize too.</typeparam>
public class InterfaceJsonDeserializer<T> : JsonConverter<T>
{
    private readonly Dictionary<string, Type> knownTypes;

    /// <summary>
    /// Create a new <see cref="InterfaceJsonDeserializer{T}"/>.
    /// </summary>
    public InterfaceJsonDeserializer(IEnumerable<Type> knownTypes)
        => this.knownTypes = knownTypes.ToDictionary(x => x.GetCustomAttribute<GraphQLTypeAttribute>()?.Name ?? x.Name);

    /// <inheritdoc/>
    public override bool HandleNull => true;

    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.Equals(typeof(T));

    /// <inheritdoc/>
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        using var jsonDocument = JsonDocument.ParseValue(ref reader);
        if (!jsonDocument.RootElement.TryGetProperty("__typename", out var typeProperty))
        {
            throw new JsonException("__typename was not returned or requested from the server.");
        }

        if (!this.knownTypes.TryGetValue(typeProperty.GetString()!, out var type))
        {
            throw new JsonException();
        }

        var result = JsonSerializer.Deserialize(jsonDocument, type, options);

        if (result is T @interface)
        {
            return @interface;
        }

        return default!;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Helpers around <see cref="JsonSerializerOptions"/>.
/// </summary>
public static class JsonSerializerOptionsExtensions
{
    /// <summary>
    /// Enable an interface of type <typeparamref name="T"/> to be correctly deserialized.
    /// </summary>
    /// <typeparam name="T">The interface type.</typeparam>
    /// <param name="opt">The current options</param>
    /// <param name="knownTypes">The types <typeparamref name="T"/> can be deserialized too.</param>
    /// <returns>The updated options.</returns>
    public static JsonSerializerOptions RegisterInterface<T>(this JsonSerializerOptions opt, IEnumerable<Type> knownTypes)
    {
        opt.Converters.Add(new InterfaceJsonDeserializer<T>(knownTypes));
        return opt;
    }

    /// <summary>
    /// Enable an interface of type <typeparamref name="T"/> to be correctly deserialized.
    /// </summary>
    /// <typeparam name="T">The interface type.</typeparam>
    /// <param name="opt">The current options</param>
    /// <param name="knownTypes">The types <typeparamref name="T"/> can be deserialized too.</param>
    /// <returns>The updated options.</returns>
    public static JsonSerializerOptions RegisterInterface<T>(this JsonSerializerOptions opt, params Type[] knownTypes)
    {
        opt.Converters.Add(new InterfaceJsonDeserializer<T>(knownTypes));
        return opt;
    }
}
