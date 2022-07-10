namespace LinQL.ClientGeneration;

internal static class Extensions
{
    public static string AttributeName(this string attribute)
        => attribute.Replace("Attribute", string.Empty);
}
