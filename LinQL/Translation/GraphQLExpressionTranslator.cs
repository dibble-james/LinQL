namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using LinQL.Expressions;

internal class GraphQLExpressionTranslator<TRoot, TData> : ExpressionVisitor
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly IndentingStringBuilder query = new StringBuilder().WithIndenting();

    public string Translate(GraphQLExpression<TRoot, TData> query)
    {
        this.Visit(query);
        return this.query.ToString();
    }

    protected override Expression VisitExtension(Expression node) => node switch
    {
        TypeFieldExpression field => this.VisitField(field),
        ScalarFieldExpression scalar => this.VisitScalar(scalar),
        _ => base.VisitExtension(node),
    };

    private Expression VisitField(TypeFieldExpression field)
    {
        this.query.Append(field.FieldName.ToCamelCase());

        if (field.Type.IsScalar())
        {
            this.query.AppendLine();
            return base.VisitExtension(field);
        }

        if (field.Arguments.Any())
        {
            this.query.AppendLine("(");
            using (this.query.Indent())
            {
                var last = field.Arguments.Last();

                foreach (var argument in field.Arguments[..^1])
                {
                    this.query.AppendLine($"{argument.Key}: {JsonSerializer.Serialize(argument.Value, SerializerOptions)},");
                }

                this.query.AppendLine($"{last.Key}: {JsonSerializer.Serialize(last.Value, SerializerOptions)}");
            }

            this.query.AppendLine(") {");
        }
        else
        {
            this.query.AppendLine(" {");
        }

        using (this.query.Indent())
        {
            if (field.Type.IsInterface)
            {
                this.query.AppendLine("__typename");
            }

            base.VisitExtension(field);
        }

        this.query.AppendLine("}");

        return field;
    }

    private Expression VisitScalar(ScalarFieldExpression scalar)
    {
        this.query.AppendLine(scalar.FieldName.ToCamelCase());

        return scalar;
    }
}


internal static class Extentions
{
    public static IndentingStringBuilder WithIndenting(this StringBuilder sb) => new(sb);
}

internal class IndentingStringBuilder
{
    private bool indentPending = true;
    private int indent;

    private readonly StringBuilder inner;

    public IndentingStringBuilder(StringBuilder stringBuilder) => this.inner = stringBuilder;

    public IndentingStringBuilder Append(string value) => this.DoIndent(sb => sb.Append(value))(false);
    public IndentingStringBuilder Append(char value) => this.DoIndent(sb => sb.Append(value))(false);
    public IndentingStringBuilder AppendLine(string value) => this.DoIndent(sb => sb.AppendLine(value))(true);
    public IndentingStringBuilder AppendLine() => this.DoIndent(sb => sb.AppendLine())(true);

    public override string ToString() => this.inner.ToString();

    public IDisposable Indent() => new IndentHandle(this);

    private Func<bool, IndentingStringBuilder> DoIndent(Action<StringBuilder> action) => reset =>
    {
        if (this.indentPending)
        {
            this.inner.Append(' ', 2 * this.indent);
        }

        this.indentPending = reset;

        action(this.inner);

        return this;
    };

    private struct IndentHandle : IDisposable
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
