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
/// A field's arguments become variables: each value is one of the declaration's lambda parameters after the
/// root, so it has nowhere to live in the document but a variable, and the variable's type is read from the
/// SDL argument rather than from the parameter. Names stay <c>var1…varN</c> in traversal order, which is the
/// same numbering the runtime translator gives them.
/// </remarks>
internal sealed class DeclaredQueryTranslator
{
    private readonly SchemaModel model;
    private readonly List<Diagnostic> problems = [];
    private readonly List<VariableDefinitionNode> variables = [];

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

        if (Parameters(lambda) is not { } declared || lambda.Body is not ExpressionSyntax body)
        {
            this.Report(lambda, "a lambda has to be an expression taking the schema's root type");

            return null;
        }

        var (parameter, values) = declared;

        var operation = this.OperationOf(rootTypeName);
        var selections = new List<ISelectionNode>();

        // __typename is unconditional on a non-scalar selection, the root included; a subscription root is
        // the one exception the spec forces, because GraphQL forbids it there.
        if (operation != OperationType.Subscription)
        {
            selections.Add(new FieldNode("__typename"));
        }

        this.AddSelections(body, rootTypeName, parameter, values, selections);

        return new DocumentNode(
        [
            new OperationDefinitionNode(
                location: null,
                name: new NameNode("linql"),
                description: null,
                operation: operation,
                variableDefinitions: this.variables,
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

    /// <summary>
    /// Reads the lambda's parameters, which are the query's receiver and then its values.
    /// </summary>
    /// <param name="lambda">The declaration's lambda.</param>
    /// <returns>
    /// The root parameter's name and the names of the parameters that become variables, in declaration
    /// order, or <see langword="null"/> when the lambda does not have the shape a query has.
    /// </returns>
    private static (string Parameter, IReadOnlyList<string> Values)? Parameters(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => (simple.Parameter.Identifier.ValueText, []),
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: >= 1 } parenthesized => (
            parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            [.. parenthesized.ParameterList.Parameters.Skip(1).Select(x => x.Identifier.ValueText)]),
        _ => null,
    };

    private static string? ParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
        _ => null,
    };

    private void AddSelections(ExpressionSyntax expression, string typeName, string parameter, IReadOnlyList<string> values, List<ISelectionNode> selections)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                this.AddSelections(parenthesized.Expression, typeName, parameter, values, selections);
                break;

            case AnonymousObjectCreationExpressionSyntax anonymous:
                foreach (var member in anonymous.Initializers)
                {
                    this.AddSelections(member.Expression, typeName, parameter, values, selections);
                }

                break;

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(Enumerable.Select):
                this.AddSelectedField(access.Expression, typeName, parameter, values, Projection(invocation), selections);
                break;

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(SelectExtentions.SelectAll):
                this.AddSelectedField(access.Expression, typeName, parameter, values, lambda: null, selections);
                break;

            default:
                this.AddLeafOrImplicitField(expression, typeName, parameter, values, selections);
                break;
        }
    }

    /// <summary>
    /// Adds a field that carries an explicit selection.
    /// </summary>
    /// <param name="receiver">The expression the <c>Select</c> or <c>SelectAll</c> is called on.</param>
    /// <param name="typeName">The type the selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="lambda">The projection, or <see langword="null"/> for <c>SelectAll</c>.</param>
    /// <param name="selections">The selection set being built.</param>
    private void AddSelectedField(
        ExpressionSyntax receiver,
        string typeName,
        string parameter,
        IReadOnlyList<string> values,
        ExpressionSyntax? lambda,
        List<ISelectionNode> selections)
    {
        if (this.FieldOf(receiver, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var childType = field.Definition.Type.NamedType().Name.Value;
        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        if (lambda is null)
        {
            children.AddRange(this.ScalarFieldsOf(childType).Select(x => Field(x, selectionSet: null)));
        }
        else if (lambda is LambdaExpressionSyntax projection && ParameterName(projection) is { } childParameter && projection.Body is ExpressionSyntax body)
        {
            this.AddSelections(body, childType, childParameter, values, children);
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
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
    private void AddLeafOrImplicitField(ExpressionSyntax expression, string typeName, string parameter, IReadOnlyList<string> values, List<ISelectionNode> selections)
    {
        if (this.FieldOf(expression, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var fieldType = field.Definition.Type.NamedType().Name.Value;

        if (!this.model.KnownScalars.ContainsKey(fieldType))
        {
            this.problems.Add(Diagnostic.Create(
                DeclaredQueryDiagnostics.ImplicitSelection,
                expression.GetLocation(),
                field.Definition.Name.Value,
                fieldType));

            return;
        }

        selections.Add(Field(field, selectionSet: null));
    }

    private IEnumerable<ResolvedField> ScalarFieldsOf(string typeName)
        => this.model.FieldsOf(typeName)
            .Where(x => this.model.KnownScalars.ContainsKey(x.Type.NamedType().Name.Value))
            .Select(x => new ResolvedField(x, []));

    /// <summary>
    /// Resolves the expression selecting a field to the field and the arguments it attaches.
    /// </summary>
    /// <param name="expression">The member access or call.</param>
    /// <param name="typeName">The type the field is selected from.</param>
    /// <param name="parameter">The lambda parameter bound to that type.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <returns>The field, or <see langword="null"/> when the expression cannot be one.</returns>
    private ResolvedField? FieldOf(ExpressionSyntax expression, string typeName, string parameter, IReadOnlyList<string> values)
    {
        switch (expression)
        {
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax call } invocation
                when IsOnParameter(call.Expression, parameter):
            {
                var field = this.FieldNamed(call.Name, typeName);

                return field is null ? null : this.FieldWithArguments(field, invocation.ArgumentList, values);
            }

            case MemberAccessExpressionSyntax access when IsOnParameter(access.Expression, parameter):
            {
                var field = this.FieldNamed(access.Name, typeName);

                return field is null ? null : new ResolvedField(field, []);
            }

            default:
                this.Report(expression, "the expression is not a field of the type being selected");

                return null;
        }
    }

    /// <summary>
    /// Resolves a C# member to the field the schema declares for it.
    /// </summary>
    /// <param name="name">The member's name.</param>
    /// <param name="typeName">The type the field is selected from.</param>
    /// <returns>The field, or <see langword="null"/> when the type declares none by that name.</returns>
    private FieldDefinitionNode? FieldNamed(SimpleNameSyntax name, string typeName)
    {
        var memberName = name.Identifier.ValueText;

        var field = this.model.FieldsOf(typeName).FirstOrDefault(x =>
            SchemaNames.FieldName(x.Name.Value) == memberName
            || SchemaNames.ExecuteName(x.Name.Value) == memberName);

        if (field is null)
        {
            this.Report(name, $"'{memberName}' is not a field of '{typeName}'");
        }

        return field;
    }

    /// <summary>
    /// Attaches a field's arguments as variables, typed from the SDL.
    /// </summary>
    /// <param name="field">The field being called.</param>
    /// <param name="arguments">The arguments the call passes.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <returns>The field with its variables, or <see langword="null"/> when an argument cannot be one.</returns>
    private ResolvedField? FieldWithArguments(FieldDefinitionNode field, ArgumentListSyntax arguments, IReadOnlyList<string> values)
    {
        var attached = new List<ArgumentNode>();

        for (var position = 0; position < arguments.Arguments.Count; position++)
        {
            var argument = arguments.Arguments[position];

            if (DeclaredArgument(field, argument, position) is not { } declared)
            {
                this.Report(
                    argument,
                    argument.NameColon is { } named
                        ? $"'{field.Name.Value}' declares no argument named '{named.Name.Identifier.ValueText}'"
                        : $"'{field.Name.Value}' declares no argument in position {position + 1}");

                return null;
            }

            // Every value becomes a variable, so a value the document cannot name a variable for is
            // reported rather than written into the document as if it had been precomputed.
            if (argument.Expression is not IdentifierNameSyntax identifier || !values.Contains(identifier.Identifier.ValueText))
            {
                this.problems.Add(Diagnostic.Create(
                    DeclaredQueryDiagnostics.UnsupportedValue,
                    argument.Expression.GetLocation(),
                    argument.Expression.ToString()));

                return null;
            }

            var name = "var" + (this.variables.Count + 1);

            this.variables.Add(
                new VariableDefinitionNode(
                    location: null,
                    variable: new VariableNode(name),
                    description: null,
                    type: declared.Type,
                    defaultValue: null,
                    directives: []));

            attached.Add(new ArgumentNode(declared.Name.Value, new VariableNode(name)));
        }

        return new ResolvedField(field, attached);
    }

    /// <summary>
    /// Resolves the SDL argument a call's argument fills.
    /// </summary>
    /// <param name="field">The field being called.</param>
    /// <param name="argument">The call's argument.</param>
    /// <param name="position">Its position in the call.</param>
    /// <returns>The argument the schema declares, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// A positional argument fills the schema's argument in the same position, because the generated
    /// <c>Execute*</c> method declares its parameters in schema order and under the schema's own names.
    /// </remarks>
    private static InputValueDefinitionNode? DeclaredArgument(FieldDefinitionNode field, ArgumentSyntax argument, int position)
        => argument.NameColon is { } named
            ? field.Arguments.FirstOrDefault(x => x.Name.Value == named.Name.Identifier.ValueText)
            : position < field.Arguments.Count ? field.Arguments[position] : null;

    private static bool IsOnParameter(ExpressionSyntax expression, string parameter)
        => expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter;

    private static ExpressionSyntax? Projection(InvocationExpressionSyntax invocation)
        => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;

    private static FieldNode Field(ResolvedField field, SelectionSetNode? selectionSet) => new(
        location: null,
        name: new NameNode(field.Definition.Name.Value),
        alias: null,
        directives: [],
        arguments: field.Arguments,
        selectionSet: selectionSet);

    private void Report(SyntaxNode node, string reason)
        => this.problems.Add(Diagnostic.Create(
            DeclaredQueryDiagnostics.UnsupportedConstruct,
            node.GetLocation(),
            node.ToString(),
            reason));

    /// <summary>
    /// A field the declaration selects, with the arguments it attaches as variables.
    /// </summary>
    /// <param name="Definition">The field the schema declares.</param>
    /// <param name="Arguments">The arguments the field is selected with, each one a variable.</param>
    private sealed record ResolvedField(FieldDefinitionNode Definition, IReadOnlyList<ArgumentNode> Arguments);
}
