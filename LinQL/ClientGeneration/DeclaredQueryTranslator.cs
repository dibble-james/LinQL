namespace LinQL.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using LinQL.Description;
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
/// A field's argument is either one of the declaration's parameters, which becomes a variable named
/// <c>var1…varN</c> in traversal order and typed from the SDL argument, or a value the compiler saw —
/// a constant, an enum member, an object or collection initialiser — which is written into the document. A
/// parameter standing inside such a value is still a variable, so values and variables mix the way GraphQL
/// input literals allow. An argument is the only place a parameter may stand: a projection that reads one is
/// reported as <see cref="DeclaredQueryDiagnostics.ProjectionReadsValue"/>, because the document does not depend
/// on the values and a projection that did would silently read the default substituted for them.
/// An <c>Includes</c> call grows the selection it is attached to rather than the document root, which is what the
/// runtime translator does, so <c>q.Hero.Includes(h => h.Name)</c> writes into <c>hero</c>'s selection set.
/// </remarks>
internal sealed class DeclaredQueryTranslator
{
    private readonly SchemaModel model;
    private readonly IReadOnlyDictionary<int, DeclaredValue> declaredValues;
    private readonly List<Diagnostic> problems = [];
    private readonly List<VariableDefinitionNode> variables = [];
    private readonly Dictionary<string, List<string>> bindings = new(StringComparer.Ordinal);
    private IReadOnlyList<string> values = [];
    private RootOperationType operation = RootOperationType.Query;

    private DeclaredQueryTranslator(SchemaModel model, IReadOnlyList<DeclaredValueAt> values)
        => (this.model, this.declaredValues) = (model, values.ToDictionary(x => x.Start, x => x.Value));

    /// <summary>
    /// Translates the body of a declared query.
    /// </summary>
    /// <param name="lambda">The declaration's lambda.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <param name="rootTypeName">The name of the schema's root type.</param>
    /// <param name="values">The values the declaration writes, as the compiler saw them.</param>
    /// <returns>
    /// The document and the variables each parameter fills, or a null document when the lambda could not be
    /// translated at all.
    /// </returns>
    public static DeclaredQueryTranslation Translate(
        LambdaExpressionSyntax? lambda,
        SchemaModel model,
        string rootTypeName,
        IReadOnlyList<DeclaredValueAt> values)
    {
        var translator = new DeclaredQueryTranslator(model, values);
        var document = translator.TranslateQuery(lambda, rootTypeName);

        // A declaration with a reported problem has no document: emitting the part that could be translated
        // would ship a query that silently differs from what the declaration says.
        return new(
            translator.problems.Count == 0 ? document : null,
            translator.operation,
            translator.Bindings(),
            translator.problems);
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

        this.values = values;

        this.operation = this.OperationOf(rootTypeName);

        var selections = new List<ISelectionNode>();

        // __typename is unconditional on a non-scalar selection, the root included; a subscription root is
        // the one exception the spec forces, because GraphQL forbids it there.
        if (this.operation != RootOperationType.Subscription)
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
                operation: DocumentOperation(this.operation),
                variableDefinitions: this.variables,
                directives: [],
                selectionSet: new SelectionSetNode(selections)),
        ]);
    }

    private RootOperationType OperationOf(string rootTypeName)
        => this.model.RootOperations.FirstOrDefault(x => x.Name == rootTypeName)?.RootOperation switch
        {
            HotChocolate.Language.OperationType.Query => RootOperationType.Query,
            HotChocolate.Language.OperationType.Mutation => RootOperationType.Mutation,
            HotChocolate.Language.OperationType.Subscription => RootOperationType.Subscription,
            _ => RootOperationType.Query,
        };

    private static OperationType DocumentOperation(RootOperationType operation) => operation switch
    {
        RootOperationType.Query => OperationType.Query,
        RootOperationType.Mutation => OperationType.Mutation,
        RootOperationType.Subscription => OperationType.Subscription,
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

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(IncludesExtensions.Includes):
                this.AddIncludedSelections(invocation, typeName, parameter, values, selections);
                break;

            case IdentifierNameSyntax identifier when values.Contains(identifier.Identifier.ValueText):
                this.ReportProjection(identifier, identifier.Identifier.ValueText);
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
    /// <remarks>
    /// A projection that is one of the declaration's values is reported as
    /// <see cref="DeclaredQueryDiagnostics.ProjectionReadsValue"/> rather than as a malformed projection, because
    /// the value is what is wrong with it: the document does not depend on the values, so the projection cannot.
    /// </remarks>
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
        else if (RootIdentifier(lambda) is { } value && values.Contains(value))
        {
            this.ReportProjection(lambda, value);

            return;
        }
        else
        {
            this.Report(receiver, "a projection has to be an expression taking the field's type");

            return;
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    /// <summary>
    /// Adds the fields an include lambda selects to the selection it is called on.
    /// </summary>
    /// <param name="invocation">The <c>Includes</c> call.</param>
    /// <param name="typeName">The type the enclosing selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
    /// <remarks>
    /// An include attaches to the selection it is called on, as the runtime translator attaches it: on a field it
    /// grows that field's selection, and on the enclosing lambda's own parameter it grows the selection that
    /// lambda is already inside. A chain is read from the outside in, so
    /// <c>q.Hero.Select(h => new { h.Name }).Includes(h => h.Friend.Select(f => new { f.Name }))</c> writes both
    /// into <c>hero</c>'s selection set. Anything else the call is made on is reported, because an include with
    /// no selection to grow would silently drop the fields it names.
    /// </remarks>
    private void AddIncludedSelections(
        InvocationExpressionSyntax invocation,
        string typeName,
        string parameter,
        IReadOnlyList<string> values,
        List<ISelectionNode> selections)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return;
        }

        var (target, inner) = SelectionsOf(access.Expression);
        List<ExpressionSyntax?> chosen = [.. inner, invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression];

        // An include on the lambda's own parameter grows the selection the lambda is already inside.
        if (IsOnParameter(target, parameter))
        {
            foreach (var selection in chosen)
            {
                this.AddIncludedSelection(selection, typeName, values, selections);
            }

            return;
        }

        if (this.FieldOf(target, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var childType = field.Definition.Type.NamedType().Name.Value;
        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        foreach (var selection in chosen)
        {
            this.AddIncludedSelection(selection, childType, values, children);
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    /// <summary>
    /// Adds one included selection to a selection set.
    /// </summary>
    /// <param name="selection">The include's argument, or <see langword="null"/> for <c>SelectAll</c>.</param>
    /// <param name="typeName">The type the selection is made on.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
    private void AddIncludedSelection(ExpressionSyntax? selection, string typeName, IReadOnlyList<string> values, List<ISelectionNode> selections)
    {
        if (selection is null)
        {
            selections.AddRange(this.ScalarFieldsOf(typeName).Select(x => Field(x, selectionSet: null)));

            return;
        }

        if (selection is LambdaExpressionSyntax lambda && ParameterName(lambda) is { } parameter && lambda.Body is ExpressionSyntax body)
        {
            this.AddSelections(body, typeName, parameter, values, selections);

            return;
        }

        if (RootIdentifier(selection) is { } name && values.Contains(name))
        {
            this.ReportProjection(selection, name);

            return;
        }

        this.Report(selection, "an include has to be an expression selecting fields of the type it is attached to");
    }

    /// <summary>
    /// Reads the selections a field expression already carries, so an include can grow them.
    /// </summary>
    /// <param name="expression">The expression the include is called on.</param>
    /// <returns>
    /// The field expression the selections belong to, and each selection's argument in source order, with
    /// <see langword="null"/> for <c>SelectAll</c>.
    /// </returns>
    private static (ExpressionSyntax Target, IReadOnlyList<ExpressionSyntax?> Selections) SelectionsOf(ExpressionSyntax expression)
    {
        List<ExpressionSyntax?> selections = [];

        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation
            && access.Name.Identifier.ValueText is var name
            && (name == nameof(IncludesExtensions.Includes) || name == nameof(Enumerable.Select) || name == nameof(SelectExtentions.SelectAll)))
        {
            selections.Add(name == nameof(SelectExtentions.SelectAll)
                ? null
                : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression);

            expression = access.Expression;
        }

        selections.Reverse();

        return (expression, selections);
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
                if (RootIdentifier(expression) is { } name && values.Contains(name))
                {
                    this.ReportProjection(expression, name);
                }
                else
                {
                    this.Report(expression, "the expression is not a field of the type being selected");
                }

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
    /// Attaches a field's arguments, each one a variable or a value the compiler saw.
    /// </summary>
    /// <param name="field">The field being called.</param>
    /// <param name="arguments">The arguments the call passes.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <returns>The field with its arguments, or <see langword="null"/> when one cannot be written.</returns>
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

            if (this.ValueOf(argument.Expression, declared.Type, values) is not { } value)
            {
                return null;
            }

            attached.Add(new ArgumentNode(declared.Name.Value, value));
        }

        return new ResolvedField(field, attached);
    }

    /// <summary>
    /// Reads an argument's value.
    /// </summary>
    /// <param name="expression">The expression the call passes.</param>
    /// <param name="expected">The type the schema declares the argument as.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <returns>The value, or <see langword="null"/> when it cannot be written.</returns>
    private IValueNode? ValueOf(ExpressionSyntax expression, ITypeNode expected, IReadOnlyList<string> values)
    {
        if (expression is IdentifierNameSyntax identifier && values.Contains(identifier.Identifier.ValueText))
        {
            return this.Variable(identifier.Identifier.ValueText, expected);
        }

        if (!this.declaredValues.TryGetValue(expression.SpanStart, out var value))
        {
            this.ReportValue(
                expression,
                "only a parameter, a constant the compiler can see, or an object or collection initialiser of those can be written into a document");

            return null;
        }

        return this.Structure(value, expected, expression);
    }

    /// <summary>
    /// Writes a value the compiler saw into the document.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="expected">The type the schema declares the position as.</param>
    /// <param name="location">Where the value was written, for a diagnostic.</param>
    /// <returns>The document node, or <see langword="null"/> when the value cannot be written.</returns>
    private IValueNode? Structure(DeclaredValue value, ITypeNode expected, SyntaxNode location) => value switch
    {
        DeclaredConstant constant => ConstantNode(constant.Constant)
            ?? this.Unsupported(location, $"'{constant.Constant}' has no form in a document"),
        DeclaredEnumMember member => new EnumValueNode(member.Member),
        DeclaredParameter parameter when this.values.Contains(parameter.Name) => this.Variable(parameter.Name, expected),
        DeclaredParameter parameter => this.Unsupported(location, $"'{parameter.Name}' is not one of the declaration's values"),
        DeclaredObject declared => this.InputObject(declared, expected, location),
        DeclaredList declared => this.ListValue(declared, expected, location),
        _ => this.Unsupported(location, "the value cannot be written into a document"),
    };

    /// <summary>
    /// Writes an object initialiser as an input object, naming each field the way the schema does.
    /// </summary>
    private IValueNode? InputObject(DeclaredObject value, ITypeNode expected, SyntaxNode location)
    {
        var typeName = expected.NamedType().Name.Value;

        if (!this.model.DeclaredTypesByName.TryGetValue(typeName, out var input) || input.Kind != DeclaredTypeKind.InputObject)
        {
            return this.Unsupported(location, $"'{typeName}' is not an input object the schema declares");
        }

        var fields = new List<ObjectFieldNode>();

        foreach (var field in value.Fields)
        {
            var declared = input.Fields.FirstOrDefault(x => SchemaNames.FieldName(x.Name.Value) == field.Name);

            if (declared is null)
            {
                return this.Unsupported(location, $"'{typeName}' declares no field named '{field.Name}'");
            }

            if (this.Structure(field.Value, declared.Type, location) is not { } node)
            {
                return null;
            }

            fields.Add(new ObjectFieldNode(declared.Name.Value, node));
        }

        return new ObjectValueNode(fields);
    }

    /// <summary>
    /// Writes a collection initialiser as a list, typing each item as the list's element.
    /// </summary>
    private IValueNode? ListValue(DeclaredList value, ITypeNode expected, SyntaxNode location)
    {
        if (!expected.IsListType() && !(expected.IsNonNullType() && expected.InnerType().IsListType()))
        {
            return this.Unsupported(location, $"'{expected}' is not a list");
        }

        var element = expected is ListTypeNode list ? list.Type : expected.InnerType() is ListTypeNode inner ? inner.Type : expected;

        var items = new List<IValueNode>();

        foreach (var item in value.Items)
        {
            if (this.Structure(item, element, location) is not { } node)
            {
                return null;
            }

            items.Add(node);
        }

        return new ListValueNode(items);
    }

    /// <summary>
    /// Declares a variable for a parameter at the position it stands in, typed from that position.
    /// </summary>
    /// <param name="parameter">The parameter's name.</param>
    /// <param name="type">The type the schema declares the position as.</param>
    /// <returns>The reference to the variable.</returns>
    private VariableNode Variable(string parameter, ITypeNode type)
    {
        var name = "var" + (this.variables.Count + 1);

        this.variables.Add(
            new VariableDefinitionNode(
                location: null,
                variable: new VariableNode(name),
                description: null,
                type: type,
                defaultValue: null,
                directives: []));

        this.Bind(parameter, name);

        return new VariableNode(name);
    }

    /// <summary>
    /// Writes a constant as the document value of its shape.
    /// </summary>
    /// <param name="constant">The constant.</param>
    /// <returns>The node, or <see langword="null"/> when the constant has no document form.</returns>
    private static IValueNode? ConstantNode(object? constant) => constant switch
    {
        null => new NullValueNode(location: null),
        string text => new StringValueNode(text),
        bool flag => new BooleanValueNode(flag),
        char character => new StringValueNode(character.ToString()),
        byte number => new IntValueNode(number),
        sbyte number => new IntValueNode(number),
        short number => new IntValueNode(number),
        ushort number => new IntValueNode(number),
        int number => new IntValueNode(number),
        uint number => new IntValueNode(number),
        long number => new IntValueNode(number),
        ulong number => new IntValueNode(number),
        float number => new FloatValueNode(number),
        double number => new FloatValueNode(number),
        decimal number => new FloatValueNode(number),
        _ => null,
    };

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

    private void Bind(string parameter, string variable)
    {
        if (!this.bindings.TryGetValue(parameter, out var variables))
        {
            variables = [];
            this.bindings[parameter] = variables;
        }

        variables.Add(variable);
    }

    /// <summary>
    /// Gets the variables each of the declaration's parameters fills.
    /// </summary>
    /// <returns>The bindings in declaration order, so the binder's parameters read as the lambda wrote them.</returns>
    private IReadOnlyList<DeclaredQueryBinding> Bindings()
        => [.. this.values.Where(this.bindings.ContainsKey).Select(x => new DeclaredQueryBinding(x, this.bindings[x]))];

    private static bool IsOnParameter(ExpressionSyntax expression, string parameter)
        => expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter;

    /// <summary>
    /// Finds the identifier an expression is built on, so a value used through a member or a call is still
    /// recognisable as that value.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The identifier's name, or <see langword="null"/> when the expression is not built on one.</returns>
    private static string? RootIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        MemberAccessExpressionSyntax access => RootIdentifier(access.Expression),
        InvocationExpressionSyntax invocation => RootIdentifier(invocation.Expression),
        ParenthesizedExpressionSyntax parenthesized => RootIdentifier(parenthesized.Expression),
        _ => null,
    };

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

    private void ReportValue(SyntaxNode node, string reason)
        => this.problems.Add(Diagnostic.Create(
            DeclaredQueryDiagnostics.UnsupportedValue,
            node.GetLocation(),
            node.ToString(),
            reason));

    private void ReportProjection(SyntaxNode node, string parameter)
        => this.problems.Add(Diagnostic.Create(
            DeclaredQueryDiagnostics.ProjectionReadsValue,
            node.GetLocation(),
            parameter));

    private IValueNode? Unsupported(SyntaxNode node, string reason)
    {
        this.ReportValue(node, reason);

        return null;
    }

    /// <summary>
    /// A field the declaration selects, with the arguments it attaches.
    /// </summary>
    /// <param name="Definition">The field the schema declares.</param>
    /// <param name="Arguments">The arguments the field is selected with, each a variable or a written value.</param>
    private sealed record ResolvedField(FieldDefinitionNode Definition, IReadOnlyList<ArgumentNode> Arguments);
}
