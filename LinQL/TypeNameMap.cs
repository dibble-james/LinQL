namespace LinQL;

using LinQL.Expressions;

/// <summary>
/// Maps CLR types to GraphQL names.
/// </summary>
public class TypeNameMap
{
    private Dictionary<Type, string> map = new();

    /// <summary>
    /// Gets the default set of type mappings
    /// </summary>
    public static TypeNameMap DefaultMappings => new()
    {
        map = new()
        {
            { typeof(short), "Int" },
            { typeof(long), "Int" },
            { typeof(int), "Int" },
            { typeof(decimal), "Float" },
            { typeof(float), "Float" },
            { typeof(double), "Float" },
            { typeof(bool), "Bool" }
        }
    };

    /// <summary>
    /// Map a <paramref name="type"/> to a <paramref name="name"/>.
    /// </summary>
    /// <param name="type">The type to map</param>
    /// <param name="name">The graphql type name</param>
    /// <returns>The new map.</returns>
    public TypeNameMap WithMap(Type type, string name)
    {
        if (this.map.ContainsKey(type))
        {
            this.map[type] = name;
        }
        else
        {
            this.map.Add(type, name);
        }

        return this;
    }

    /// <summary>
    /// Get any registered graphql type name from a CLR type.
    /// </summary>
    /// <param name="variable">The type to find a map for.</param>
    /// <returns>The graphql type name.</returns>
    public string GetTypeName(Variable variable)
    {
        if (!this.map.TryGetValue(variable.Type, out var name))
        {
            name = variable.Type.Name;
        }

        return $"{name}{(variable.Nullable ? "" : "!")}";
    }
}
