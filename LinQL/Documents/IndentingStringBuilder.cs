namespace LinQL.Documents;

using System.Text;

/// <summary>
/// Extension methods for <see cref="StringBuilder"/>.
/// </summary>
internal static class StringBuilderExtensions
{
    /// <summary>
    /// Wraps the builder in an <see cref="IndentingStringBuilder"/>.
    /// </summary>
    /// <param name="sb">The builder to wrap.</param>
    /// <returns>The wrapped builder.</returns>
    public static IndentingStringBuilder WithIndenting(this StringBuilder sb) => new(sb);
}

/// <summary>
/// A <see cref="StringBuilder"/> that keeps track of line indentation.
/// </summary>
/// <param name="stringBuilder">The underlying builder.</param>
internal class IndentingStringBuilder(StringBuilder stringBuilder)
{
    private bool indentPending = true;
    private int indent;

    /// <summary>
    /// Appends a value to the current line.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <returns>This builder.</returns>
    public IndentingStringBuilder Append(string value) => this.DoIndent(sb => sb.Append(value))(false);

    /// <summary>
    /// Appends a character to the current line.
    /// </summary>
    /// <param name="value">The character to append.</param>
    /// <returns>This builder.</returns>
    public IndentingStringBuilder Append(char value) => this.DoIndent(sb => sb.Append(value))(false);

    /// <summary>
    /// Appends a value and a line break.
    /// </summary>
    /// <param name="value">The value to append.</param>
    /// <returns>This builder.</returns>
    public IndentingStringBuilder AppendLine(string value) => this.DoIndent(sb => sb.AppendLine(value))(true);

    /// <summary>
    /// Appends a line break.
    /// </summary>
    /// <returns>This builder.</returns>
    public IndentingStringBuilder AppendLine() => this.DoIndent(sb => sb.AppendLine())(true);

    /// <inheritdoc />
    public override string ToString() => stringBuilder.ToString();

    /// <summary>
    /// Indents everything appended until the returned handle is disposed.
    /// </summary>
    /// <returns>The handle that ends the indentation.</returns>
    public IDisposable Indent() => new IndentHandle(this);

    private Func<bool, IndentingStringBuilder> DoIndent(Action<StringBuilder> action) => reset =>
    {
        if (this.indentPending)
        {
            stringBuilder.Append(' ', 2 * this.indent);
        }

        this.indentPending = reset;

        action(stringBuilder);

        return this;
    };

    private readonly struct IndentHandle : IDisposable
    {
        private readonly IndentingStringBuilder indenter;

        public IndentHandle(IndentingStringBuilder indenter)
        {
            this.indenter = indenter;
            indenter.indent++;
        }

        public void Dispose() => this.indenter.indent--;
    }
}
