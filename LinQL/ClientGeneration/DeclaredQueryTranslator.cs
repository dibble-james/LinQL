namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Translates a declared query's lambda into the document model, reading the schema from
/// <see cref="SchemaModel"/> instead of reflection.
/// </summary>
/// <remarks>
/// The pass only reads the lambda's syntax: a source generator has no <c>Expression</c> tree, so the
/// runtime translator cannot be used. What it can support is decided here and nothing is approximated —
/// a construct the translator does not handle is reported as <see cref="DeclaredQueryDiagnostics.UnsupportedConstruct"/>
/// rather than dropped from the document, because a document that looks precomputed and is not is the one
/// outcome the compile-time path cannot ship.
/// </remarks>
internal sealed class DeclaredQueryTranslator
{
    private readonly SchemaModel model;
    private readonly List<Diagnostic> problems = [];

    private DeclaredQueryTranslator(SchemaModel model) => this.model = model;

    /// <summary>
    /// Translates the body of a declared query.
    /// </summary>
    /// <param name="lambda">The declaration's lambda.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <param name="rootTypeName">The name of the schema's root type.</param>
    /// <returns>The document, or <see langword="null"/> when the lambda could not be translated at all.</returns>
    public static (DocumentNode? Document, IReadOnlyList<Diagnostic> Problems) Translate(LambdaExpressionSyntax? lambda, SchemaModel model, string rootTypeName)
    {
        var translator = new DeclaredQueryTranslator(model);
        var document = translator.TranslateQuery(lambda, rootTypeName);

        // A declaration with a reported problem has no document: emitting the part that could be translated
        // would ship a query that silently differs from what the declaration says.
        return (translator.problems.Count == 0 ? document : null, translator.problems);
    }

    private DocumentNode? TranslateQuery(LambdaExpressionSyntax? lambda, string rootTypeName)
    {
        if (lambda is null)
        {
            return null;
        }

        var parameter = ParameterName(lambda);

        if (parameter is null || lambda.Body is not ExpressionSyntax body)
        {
            this.Report(lambda, "a lambda has to be an expression taking the schema's root type");

            return null;
        }

        var operation = this.OperationOf(rootTypeName);
        var selections = new List<ISelectionNode>();

        // __typename is unconditional on a non-scalar selection, the root included; a subscription root is
        // the one exception the spec forces, because GraphQL forbids it there.
        if (operation != OperationType.Subscription)
        {
            selections.Add(new FieldNode("__typename"));
        }

        this.AddSelections(body, rootTypeName, parameter, selections);

        return new DocumentNode(
        [
            new OperationDefinitionNode(
                location: null,
                name: new NameNode("linql"),
                description: null,
                operation: operation,
                variableDefinitions: [],
                directives: [],
                selectionSet: new SelectionSetNode(selections)),
        ]);
    }

    private OperationType OperationOf(string rootTypeName)
        => this.model.RootOperations.FirstOrDefault(x => x.Name == rootTypeName)?.RootOperation switch
        {
            HotChocolate.Language.OperationType.Query => OperationType.Query,
            HotChocolate.Language.OperationType.Mutation => OperationType.Mutation,
            HotChocolate.Language.OperationType.Subscription => OperationType.Subscription,
            _ => OperationType.Query,
        };

    private static string? ParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
        _ => null,
    };

    private void AddSelections(ExpressionSyntax expression, string typeName, string parameter, List<ISelectionNode> selections)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                this.AddSelections(parenthesized.Expression, typeName, parameter, selections);
                break;

            case AnonymousObjectCreationExpressionSyntax anonymous:
                foreach (var member in anonymous.Initializers)
                {
                    this.AddSelections(member.Expression, typeName, parameter, selections);
                }

                break;

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(Enumerable.Select):
                this.AddSelectedField(access.Expression, typeName, parameter, Projection(invocation), selections);
                break;

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(SelectExtentions.SelectAll):
                this.AddSelectedField(access.Expression, typeName, parameter, lambda: null, selections);
                break;

            default:
                this.AddLeafOrImplicitField(expression, typeName, parameter, selections);
                break;
        }
    }

    /// <summary>
    /// Adds a field that carries an explicit selection.
    /// </summary>
    /// <param name="receiver">The expression the <c>Select</c> or <c>SelectAll</c> is called on.</param>
    /// <param name="typeName">The type the selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="lambda">The projection, or <see langword="null"/> for <c>SelectAll</c>.</param>
    /// <param name="selections">The selection set being built.</param>
    private void AddSelectedField(
        ExpressionSyntax receiver,
        string typeName,
        string parameter,
        ExpressionSyntax? lambda,
        List<ISelectionNode> selections)
    {
        if (this.FieldOf(receiver, typeName, parameter) is not { } field)
        {
            return;
        }

        var childType = field.Type.NamedType().Name.Value;
        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        if (lambda is null)
        {
            children.AddRange(this.ScalarFieldsOf(childType));
        }
        else if (lambda is LambdaExpressionSyntax projection && ParameterName(projection) is { } childParameter && projection.Body is ExpressionSyntax body)
        {
            this.AddSelections(body, childType, childParameter, children);
        }
        else
        {
            this.Report(receiver, "a projection has to be an expression taking the field's type");

            return;
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    /// <summary>
    /// Adds a field that has no selection of its own.
    /// </summary>
    /// <param name="expression">The member access or call selecting the field.</param>
    /// <param name="typeName">The type the selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="selections">The selection set being built.</param>
    private void AddLeafOrImplicitField(ExpressionSyntax expression, string typeName, string parameter, List<ISelectionNode> selections)
    {
        if (this.FieldOf(expression, typeName, parameter) is not { } field)
        {
            return;
        }

        var fieldType = field.Type.NamedType().Name.Value;

        if (!this.model.KnownScalars.ContainsKey(fieldType))
        {
            this.problems.Add(Diagnostic.Create(
                DeclaredQueryDiagnostics.ImplicitSelection,
                expression.GetLocation(),
                field.Name.Value,
                fieldType));

            return;
        }

        selections.Add(Field(field, selectionSet: null));
    }

    private IEnumerable<FieldNode> ScalarFieldsOf(string typeName)
        => this.model.FieldsOf(typeName)
            .Where(x => this.model.KnownScalars.ContainsKey(x.Type.NamedType().Name.Value))
            .Select(x => Field(x, selectionSet: null));

    /// <summary>
    /// Resolves a C# member to the field the schema declares for it.
    /// </summary>
    /// <param name="expression">The member access or call.</param>
    /// <param name="typeName">The type the field is selected from.</param>
    /// <param name="parameter">The lambda parameter bound to that type.</param>
    /// <returns>The field, or <see langword="null"/> when the expression cannot be one.</returns>
    private FieldDefinitionNode? FieldOf(ExpressionSyntax expression, string typeName, string parameter)
    {
        if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax call } invocation
            && IsOnParameter(call.Expression, parameter))
        {
            // Arguments are the next step's work: their values have to become variables, and until that
            // exists an argument list cannot be translated rather than being left out of the document.
            this.Report(invocation.ArgumentList.Arguments.Any() ? invocation.ArgumentList.Arguments[0] : invocation, "field arguments are not translated yet");

            return null;
        }

        if (expression is not MemberAccessExpressionSyntax access || !IsOnParameter(access.Expression, parameter))
        {
            this.Report(expression, "the expression is not a field of the type being selected");

            return null;
        }

        var memberName = access.Name.Identifier.ValueText;
        var field = this.model.FieldsOf(typeName)
            .FirstOrDefault(x => SchemaNames.FieldName(x.Name.Value) == memberName);

        if (field is null)
        {
            this.Report(access, $"'{memberName}' is not a field of '{typeName}'");
        }

        return field;
    }

    private static bool IsOnParameter(ExpressionSyntax expression, string parameter)
        => expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter;

    private static ExpressionSyntax? Projection(InvocationExpressionSyntax invocation)
        => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;

    private static FieldNode Field(FieldDefinitionNode field, SelectionSetNode? selectionSet) => new(
        location: null,
        name: new NameNode(field.Name.Value),
        alias: null,
        directives: [],
        arguments: [],
        selectionSet: selectionSet);

    private void Report(SyntaxNode node, string reason)
        => this.problems.Add(Diagnostic.Create(
            DeclaredQueryDiagnostics.UnsupportedConstruct,
            node.GetLocation(),
            node.ToString(),
            reason));
}
