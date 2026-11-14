namespace LinQL;

/// <summary>
/// Marks a type a query can select fields from, which is what <c>Includes</c> requires of its receiver.
/// </summary>
/// <remarks>
/// The generated client implements this on every type it emits from the schema's object, interface and
/// union types, so the constraint on <c>Includes</c> rejects a receiver that has no selection — a scalar,
/// an input object, or an arbitrary CLR type — instead of failing when the query is translated. A
/// hand-written client type implements it to opt in.
/// </remarks>
public interface ICanHaveIncludes
{
}
