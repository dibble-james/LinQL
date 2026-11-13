namespace LinQL.ClientGeneration;

/// <summary>
/// The kind of declaration the generator emits for the SDL's object types, input objects and root types.
/// </summary>
/// <remarks>
/// Selected at build time by the <c>LinQLTypesAs</c> property, which is read from
/// <c>build_property.LinQLTypesAs</c>. Interfaces stay interfaces and the filter builders stay classes
/// whichever value is used.
/// </remarks>
public enum LinQLTypesAs
{
    /// <summary>
    /// Generate <c>partial class</c> declarations. This is the default.
    /// </summary>
    Class,

    /// <summary>
    /// Generate <c>partial record</c> declarations, so the generated types get value equality,
    /// <c>with</c> expressions and a generated <c>ToString</c>.
    /// </summary>
    Record,
}
