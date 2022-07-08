namespace LinQL.ClientGeneration;

/// <summary>
/// Converts SDL into a <see cref="Graph"/>.
/// </summary>
public static class ClientGenerator
{
    /// <summary>
    /// Takes a SDL input and generates a <see cref="Graph"/>.
    /// </summary>
    /// <param name="sdl">The SDL</param>
    /// <param name="graphName">The name of the <see cref="Graph"/>.</param>
    /// <param name="graphNamespace">The namespace to put the <see cref="Graph"/>.</param>
    /// <returns>The generated <see cref="Graph"/></returns>
    public static string Generate(string sdl, string graphName, string graphNamespace)
    {
        var document = HotChocolate.Language.Utf8GraphQLParser.Parse(sdl);

        var clientContext = new DocumentWalkerContext(graphName, graphNamespace);

        new DocumentWalker().Visit(document, clientContext);

        return clientContext.ToString();
    }
}
