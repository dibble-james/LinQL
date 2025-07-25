namespace LinQL.Translation;

using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using LinQL.Expressions;

/// <summary>
/// Translates <see cref="GraphQLExpression{TRoot, TResult}"/>s.
/// </summary>
public static class GraphQLExpressionTranslator
{
    /// <summary>
    /// Extract a <see cref="LinqQLRequest{TRoot, TData}"/> from the given <paramref name="expression"/>.
    /// </summary>
    /// <typeparam name="TRoot">The root operation type.</typeparam>
    /// <typeparam name="TData">The result type.</typeparam>
    /// <param name="expression">The expression to translate.</param>
    /// <returns>The request.</returns>
    public static LinqQLRequest<TRoot, TData> Translate<TRoot, TData>(GraphQLExpression<TRoot, TData> expression)
        where TRoot : RootType<TRoot>
    {
        var translator = new GraphQLExpressionTranslator<TRoot, TData>();

        var query = translator.Translate(expression);

        return new(expression, query, expression.Variables.ToDictionary(x => x.Name, x => x.Value));
    }
}

internal class GraphQLExpressionTranslator<TRoot, TData> : ExpressionVisitor
    where TRoot : RootType<TRoot>
{
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

        if (field is IRootExpression root && root.Variables.Any())
        {
            this.query.AppendLine(" linql(");
            using (this.query.Indent())
            {
                var last = root.Variables.Last();

                foreach (var variable in root.Variables.Take(root.Variables.Count - 1))
                {
                    this.query.AppendLine($"${variable.Name}: {variable.Type},");
                }

                this.query.AppendLine($"${last.Name}: {last.Type}");
            }

            this.query.AppendLine(")");
        }

        if (field.Type.IsScalar(field.Root.Scalars) && !field.Arguments.Any() && field.DeclaringType.IsRootOperation())
        {
            this.query.AppendLine(" {");

            var indent = this.query.Indent();

            try
            {
                return base.VisitExtension(field);
            }
            finally
            {
                indent.Dispose();
                this.query.AppendLine("}");
            }
        }

        if (field.Type.IsScalar(field.Root.Scalars) && !field.Arguments.Any())
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

                foreach (var argument in field.Arguments.Take(field.Arguments.Count - 1))
                {
                    this.query.AppendLine($"{argument.Key}: ${argument.Value},");
                }

                this.query.AppendLine($"{last.Key}: ${last.Value}");
            }

            this.query.AppendLine(") {");
        }
        else
        {
            this.query.AppendLine(" {");
        }

        using (this.query.Indent())
        {
            if (field.Type.RequiresTypeName())
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
        if (scalar.Arguments.Any())
        {
            this.query.AppendLine($"{scalar.FieldName.ToCamelCase()}(");
            using (this.query.Indent())
            {
                var last = scalar.Arguments.Last();

                foreach (var argument in scalar.Arguments.Take(scalar.Arguments.Count - 1))
                {
                    this.query.AppendLine($"{argument.Key}: ${argument.Value},");
                }

                this.query.AppendLine($"{last.Key}: ${last.Value}");
            }

            this.query.AppendLine(")");
        }
        else
        {
            this.query.AppendLine(scalar.FieldName.ToCamelCase());
        }

        return scalar;
    }
}


internal static class Extentions
{
    private static readonly Regex PropertyRegex = new(@"""([\w|\d]+)"":", RegexOptions.Compiled);

    public static IndentingStringBuilder WithIndenting(this StringBuilder sb) => new(sb);

    public static string UnquotePropertyNames(this string s) => PropertyRegex.Replace(s, "$1:");
}

internal class IndentingStringBuilder(StringBuilder stringBuilder)
{
    private bool indentPending = true;
    private int indent;

    public IndentingStringBuilder Append(string value) => this.DoIndent(sb => sb.Append(value))(false);
    public IndentingStringBuilder Append(char value) => this.DoIndent(sb => sb.Append(value))(false);
    public IndentingStringBuilder AppendLine(string value) => this.DoIndent(sb => sb.AppendLine(value))(true);
    public IndentingStringBuilder AppendLine() => this.DoIndent(sb => sb.AppendLine())(true);

    public override string ToString() => stringBuilder.ToString();

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
