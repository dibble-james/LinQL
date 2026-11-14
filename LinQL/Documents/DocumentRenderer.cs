namespace LinQL.Documents;

using System.Text;
using HotChocolate.Language;

/// <summary>
/// Renders the shared document model, a Hot Chocolate <see cref="OperationDefinitionNode"/>, as
/// GraphQL text.
/// </summary>
/// <remarks>
/// The model is Hot Chocolate's syntax tree so both translators build the same nodes, but the
/// formatting is LinQL's own: Hot Chocolate's serializer prints variable definitions and arguments
/// inline, which would change every document, operation cache key and <c>LinQLDocument</c> hash.
/// Keeping the writer here leaves the documents byte-identical while the representation underneath
/// changes.
/// </remarks>
internal static class DocumentRenderer
{
    /// <summary>
    /// Renders the given <paramref name="operation"/>.
    /// </summary>
    /// <param name="operation">The operation to render.</param>
    /// <returns>The GraphQL text.</returns>
    public static string Render(OperationDefinitionNode operation)
    {
        var query = new StringBuilder().WithIndenting();

        query.Append(GetKeyword(operation.Operation));
        query.Append(' ');
        query.Append(operation.Name?.Value ?? throw new InvalidOperationException("An operation must be named to be rendered."));

        RenderVariables(query, operation.VariableDefinitions);

        // The opening brace keeps its leading space, which is what every document has carried since
        // the renderer was extracted; moving it would change every cache key and belongs to its own
        // change.
        query.AppendLine(" {");

        using (query.Indent())
        {
            RenderSelections(query, operation.SelectionSet.Selections);
        }

        query.AppendLine("}");

        return query.ToString();
    }

    private static string GetKeyword(OperationType operation) => operation switch
    {
        OperationType.Mutation => "mutation",
        OperationType.Subscription => "subscription",
        OperationType.Query => "query",
        _ => throw new NotSupportedException(),
    };

    private static void RenderVariables(IndentingStringBuilder query, IReadOnlyList<VariableDefinitionNode> variables)
    {
        if (variables.Count == 0)
        {
            return;
        }

        query.AppendLine("(");

        using (query.Indent())
        {
            for (var i = 0; i < variables.Count; i++)
            {
                var variable = variables[i];
                var separator = i < variables.Count - 1 ? "," : string.Empty;

                query.AppendLine($"${variable.Variable.Name.Value}: {FormatType(variable.Type)}{separator}");
            }
        }

        query.AppendLine(")");
    }

    private static string FormatType(ITypeNode type) => type switch
    {
        NonNullTypeNode nonNull => $"{FormatType(nonNull.Type)}!",
        ListTypeNode list => $"[{FormatType(list.Type)}]",
        NamedTypeNode named => named.Name.Value,
        _ => throw new NotSupportedException($"'{type.Kind}' is not a type reference."),
    };

    private static void RenderSelections(IndentingStringBuilder query, IReadOnlyList<ISelectionNode> selections)
    {
        foreach (var selection in selections)
        {
            RenderField(
                query,
                selection as FieldNode
                    ?? throw new NotSupportedException($"'{selection.Kind}' selections are not supported yet."));
        }
    }

    private static void RenderField(IndentingStringBuilder query, FieldNode field)
    {
        query.Append(field.Name.Value);

        var hasArguments = field.Arguments.Count > 0;

        if (hasArguments)
        {
            RenderArguments(query, field.Arguments);
        }

        if (field.SelectionSet is null)
        {
            query.AppendLine(hasArguments ? ")" : string.Empty);
            return;
        }

        query.AppendLine(hasArguments ? ") {" : " {");

        using (query.Indent())
        {
            RenderSelections(query, field.SelectionSet.Selections);
        }

        query.AppendLine("}");
    }

    private static void RenderArguments(IndentingStringBuilder query, IReadOnlyList<ArgumentNode> arguments)
    {
        query.AppendLine("(");

        using (query.Indent())
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var variable = argument.Value as VariableNode
                    ?? throw new NotSupportedException($"The argument '{argument.Name.Value}' must be a variable.");
                var separator = i < arguments.Count - 1 ? "," : string.Empty;

                query.AppendLine($"{argument.Name.Value}: ${variable.Name.Value}{separator}");
            }
        }
    }
}
