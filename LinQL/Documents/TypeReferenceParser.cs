namespace LinQL.Documents;

using HotChocolate.Language;

// Parses the GraphQL type reference the generator writes into an argument attribute.
//
// The document model is Hot Chocolate's syntax tree, whose variable definitions carry an ITypeNode. The compile-
// time translator reads those nodes straight from the SDL; the runtime path only has the GQLType string the
// generator emitted, so this is the one place a type reference is read back into a node.
internal static class TypeReferenceParser
{
    /// <summary>
    /// Parses a GraphQL type reference such as <c>String!</c> or <c>[Film!]</c>.
    /// </summary>
    /// <param name="type">The type reference to parse.</param>
    /// <returns>The parsed type node.</returns>
    /// <exception cref="FormatException">The value is not a GraphQL type reference.</exception>
    public static ITypeNode Parse(string type)
    {
        var position = 0;
        var node = ParseType(type, ref position);

        SkipWhitespace(type, ref position);

        return position == type.Length
            ? node
            : throw new FormatException($"'{type}' is not a GraphQL type reference.");
    }

    private static ITypeNode ParseType(string source, ref int position)
    {
        SkipWhitespace(source, ref position);

        if (position == source.Length)
        {
            throw new FormatException($"'{source}' is missing a type.");
        }

        ITypeNode type;

        if (source[position] == '[')
        {
            position++;
            type = new ListTypeNode(ParseType(source, ref position));
            Expect(source, ref position, ']');
        }
        else
        {
            type = new NamedTypeNode(ParseName(source, ref position));
        }

        SkipWhitespace(source, ref position);

        if (position < source.Length && source[position] == '!')
        {
            position++;

            type = type is INullableTypeNode nullable
                ? new NonNullTypeNode(nullable)
                : throw new FormatException($"'{source}' repeats the non-null marker.");
        }

        return type;
    }

    private static string ParseName(string source, ref int position)
    {
        var start = position;

        if (position == source.Length || !IsNameStart(source[position]))
        {
            throw new FormatException($"'{source}' is missing a type name.");
        }

        while (position < source.Length && IsNamePart(source[position]))
        {
            position++;
        }

        return source.Substring(start, position - start);
    }

    private static void Expect(string source, ref int position, char expected)
    {
        SkipWhitespace(source, ref position);

        if (position == source.Length || source[position] != expected)
        {
            throw new FormatException($"'{source}' is missing '{expected}'.");
        }

        position++;
    }

    private static void SkipWhitespace(string source, ref int position)
    {
        while (position < source.Length && source[position] is ' ' or '\t' or '\r' or '\n')
        {
            position++;
        }
    }

    // A GraphQL name is ASCII: [_A-Za-z][_0-9A-Za-z]*.
    private static bool IsNameStart(char value) => value is '_' or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static bool IsNamePart(char value) => IsNameStart(value) || value is >= '0' and <= '9';
}
