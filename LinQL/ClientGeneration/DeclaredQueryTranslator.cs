namespace LinQL.ClientGeneration;

using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Language;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
/// runtime translator does, so <c>q.Hero.Includes(h => h.Name)</c> writes into <c>hero</c>'s selection set. An
/// <c>On</c> spread attaches in the same way and writes an inline fragment, so <c>q.Result.On((Film f) => f.Title)</c>
/// writes a fragment on <c>Film</c> into <c>result</c>'s selection set. A cast and an <c>OfType</c> write the same fragment by a
/// shorter route — <c>((Film)q.Result).Title</c> and <c>q.Results.OfType&lt;Film&gt;().Select(f => f.Title)</c> — because on the
/// wire both are a type condition over the field's selection.
/// </remarks>
internal sealed class DeclaredQueryTranslator
{
    private readonly SchemaModel model;
    private readonly IReadOnlyDictionary<int, DeclaredValue> declaredValues;
    private readonly List<Diagnostic> problems = [];
    private readonly List<VariableDefinitionNode> variables = [];
    private readonly List<DeclaredQueryBinding> bindings = [];
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
            OperationType.Query => RootOperationType.Query,
            OperationType.Mutation => RootOperationType.Mutation,
            OperationType.Subscription => RootOperationType.Subscription,
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
            case { } wrapped when CastOf(wrapped) is { } cast:
                this.AddCastSelections(expression, cast, typeName, parameter, values, selections);
                break;

            case { } wrapped when OfTypeOf(wrapped) is { } ofType:
                this.AddOfTypeSelections(expression, ofType, typeName, parameter, values, selections);
                break;

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

            case InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax access
                && access.Name.Identifier.ValueText == nameof(SelectExtentions.On):
                this.AddSpreadSelections(invocation, typeName, parameter, values, selections);
                break;

            case IdentifierNameSyntax identifier when values.Contains(identifier.Identifier.ValueText):
                this.ReportProjection(identifier, identifier.Identifier.ValueText);
                break;

            default:
                this.AddLeafOrImplicitField(expression, typeName, parameter, values, selections);
                break;
        }
    }

    /// <remarks>
    /// A projection that is one of the declaration's values is reported as
    /// <see cref="DeclaredQueryDiagnostics.ProjectionReadsValue"/> rather than as a malformed projection, because
    /// the value is what is wrong with it: the document does not depend on the values, so the projection cannot.
    /// </remarks>
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

    /// <remarks>
    /// A cast is a spread: <c>((Film)q.Result).Title</c> writes <c>result</c>'s selection as a fragment on <c>Film</c>
    /// holding <c>title</c>, and a cast applied to a lambda's own parameter writes the fragment into the selection
    /// that lambda is already inside. The selections inside the fragment are the ones the cast was applied to with
    /// the cast replaced by the parameter, so they are read against the fragment's type instead of the field's.
    /// A cast with nothing selected from it is the fragment's scalar fields, which is what <c>SelectAll</c> writes.
    /// </remarks>
    private void AddCastSelections(
        ExpressionSyntax expression,
        (SyntaxNode Node, ExpressionSyntax Operand, string? Type) cast,
        string typeName,
        string parameter,
        IReadOnlyList<string> values,
        List<ISelectionNode> selections)
    {
        var inner = cast.Node == expression
            ? null
            : expression.ReplaceNode(cast.Node, SyntaxFactory.IdentifierName(parameter));

        if (IsOnParameter(cast.Operand, parameter))
        {
            if (this.InlineFragment(cast.Type, cast.Node, (fragment, children) => this.AddFragmentSelections(inner, fragment, parameter, values, children)) is { } spread)
            {
                selections.Add(spread);
            }

            return;
        }

        if (this.FieldOf(cast.Operand, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        if (this.InlineFragment(cast.Type, cast.Node, (fragment, innerChildren) => this.AddFragmentSelections(inner, fragment, parameter, values, innerChildren)) is { } castFragment)
        {
            children.Add(castFragment);
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    private void AddFragmentSelections(ExpressionSyntax? inner, string typeName, string parameter, IReadOnlyList<string> values, List<ISelectionNode> selections)
    {
        if (inner is null)
        {
            selections.AddRange(this.ScalarFieldsOf(typeName).Select(x => Field(x, selectionSet: null)));

            return;
        }

        this.AddSelections(inner, typeName, parameter, values, selections);
    }

    /// <remarks>
    /// <c>OfType</c> filters a collection to one of its possible types, so the fragment belongs to the field's
    /// selection and whatever is chained after the call selects from the filtered type. With nothing chained the
    /// fragment selects that type's scalars, which is what the runtime path writes.
    /// </remarks>
    private void AddOfTypeSelections(
        ExpressionSyntax expression,
        InvocationExpressionSyntax ofType,
        string typeName,
        string parameter,
        IReadOnlyList<string> values,
        List<ISelectionNode> selections)
    {
        var (target, inner) = SelectionsOf(expression);

        if (target != ofType || ofType.Expression is not MemberAccessExpressionSyntax access)
        {
            this.Report(expression, "'OfType' has to be called on a field, with nothing but selections between it and the field");

            return;
        }

        if (this.FieldOf(access.Expression, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var fragmentType = access.Name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic
            ? TypeName(generic.TypeArgumentList.Arguments[0])
            : null;

        List<ExpressionSyntax?> chosen = inner.Count == 0 ? [null] : [.. inner];

        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        if (this.InlineFragment(fragmentType, ofType, (fragment, fragmentChildren) =>
        {
            foreach (var selection in chosen)
            {
                this.AddIncludedSelection(selection, fragment, values, fragmentChildren);
            }
        }) is { } ofTypeFragment)
        {
            children.Add(ofTypeFragment);
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    private ISelectionNode? InlineFragment(string? typeName, SyntaxNode location, Action<string, List<ISelectionNode>> children)
    {
        if (typeName is null)
        {
            this.Report(location, "the type a fragment is written on has to be one the syntax names");

            return null;
        }

        if (!this.model.DeclaredTypesByName.ContainsKey(typeName))
        {
            this.Report(location, $"the schema declares no type named '{typeName}' to write a fragment on");

            return null;
        }

        var selections = new List<ISelectionNode> { new FieldNode("__typename") };

        children(typeName, selections);

        return new InlineFragmentNode(
            location: null,
            typeCondition: new NamedTypeNode(typeName),
            directives: [],
            selectionSet: new SelectionSetNode(selections));
    }

    /// <remarks>
    /// Like an include, a spread attaches to the selection it is called on: on a field it writes the fragments into
    /// that field's selection, and on the enclosing lambda's own parameter into the selection the lambda is already
    /// inside. A chain writes one sibling fragment per call, because the runtime translator resolves each
    /// <c>On</c> to its own spread.
    /// </remarks>
    private void AddSpreadSelections(
        InvocationExpressionSyntax invocation,
        string typeName,
        string parameter,
        IReadOnlyList<string> values,
        List<ISelectionNode> selections)
    {
        var (target, spreads) = SpreadsOf(invocation);

        if (IsOnParameter(target, parameter))
        {
            foreach (var spread in spreads)
            {
                if (this.Fragment(spread, values) is { } fragment)
                {
                    selections.Add(fragment);
                }
            }

            return;
        }

        if (this.FieldOf(target, typeName, parameter, values) is not { } field)
        {
            return;
        }

        var children = new List<ISelectionNode> { new FieldNode("__typename") };

        foreach (var spread in spreads)
        {
            if (this.Fragment(spread, values) is { } fragment)
            {
                children.Add(fragment);
            }
        }

        selections.Add(Field(field, new SelectionSetNode(children)));
    }

    /// <remarks>
    /// The fragment's type is read from the lambda's parameter, because a generator has no semantic model to ask
    /// what the extension's constraint resolved to. The type has to be one the schema declares, since the document
    /// names it; whether the type can actually be a member of the union is left to the server, as every other name
    /// in the document is.
    /// </remarks>
    private ISelectionNode? Fragment(ExpressionSyntax spread, IReadOnlyList<string> values)
    {
        if (spread is not LambdaExpressionSyntax lambda
            || ParameterName(lambda) is not { } parameter
            || lambda.Body is not ExpressionSyntax body)
        {
            this.Report(spread, "a spread has to be a lambda selecting the type it spreads as");

            return null;
        }

        if (SpreadTypeName(lambda) is not { } typeName)
        {
            this.Report(lambda, "a spread has to name the type it spreads as, the way (Film film) => film.Title does");

            return null;
        }

        return this.InlineFragment(typeName, lambda, (fragment, children) => this.AddSelections(body, fragment, parameter, values, children));
    }

    /// <summary>
    /// Reads a chain of spreads from the outside in.
    /// </summary>
    /// <returns>
    /// The expression the chain is called on, and each spread's argument in source order.
    /// </returns>
    private static (ExpressionSyntax Target, IReadOnlyList<ExpressionSyntax> Spreads) SpreadsOf(InvocationExpressionSyntax invocation)
    {
        List<ExpressionSyntax> spreads = [];
        ExpressionSyntax target = invocation;

        while (target is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call
            && access.Name.Identifier.ValueText == nameof(SelectExtentions.On))
        {
            if (call.ArgumentList.Arguments.FirstOrDefault()?.Expression is { } spread)
            {
                spreads.Add(spread);
            }

            target = access.Expression;
        }

        spreads.Reverse();

        return (target, spreads);
    }

    private static string? SpreadTypeName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => TypeName(simple.Parameter.Type),
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized
            => TypeName(parenthesized.ParameterList.Parameters[0].Type),
        _ => null,
    };

    private static string? TypeName(TypeSyntax? type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => TypeName(qualified.Right),
        AliasQualifiedNameSyntax alias => TypeName(alias.Name),
        NullableTypeSyntax nullable => TypeName(nullable.ElementType),
        _ => null,
    };

    /// <summary>
    /// Adds a field that has no selection of its own.
    /// </summary>
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
                // A field can carry arguments from the extensions the generator emits for them, such as
                // q.Products.Filter(…) and q.Products.Sort(…), so the call is read against the field it extends.
                if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax applied } appliedCall)
                {
                    var reported = this.problems.Count;
                    var receiver = this.FieldOf(applied.Expression, typeName, parameter, values);

                    if (receiver is not null)
                    {
                        return this.AttachArgument(receiver, applied.Name, appliedCall.ArgumentList, values);
                    }

                    // The receiver was already reported, so reporting the call as well would only repeat it.
                    if (this.problems.Count != reported)
                    {
                        return null;
                    }
                }

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

    /// <remarks>
    /// The extensions the generator emits are named after the arguments they attach, except that every filter is
    /// called <c>Filter</c>: a filter's argument is therefore found by its filter input type, and <c>Sort</c> is read
    /// as the schema's <c>order</c>. A filter is one variable holding the whole filter, so it is rebuilt by the
    /// binder rather than bound as a parameter; every other argument goes through the same value reading a field's
    /// own argument does.
    /// </remarks>
    private ResolvedField? AttachArgument(
        ResolvedField receiver,
        SimpleNameSyntax name,
        ArgumentListSyntax arguments,
        IReadOnlyList<string> values)
    {
        var method = name.Identifier.ValueText;
        var field = receiver.Definition.Name.Value;

        InputValueDefinitionNode? argument;

        if (method == "Filter")
        {
            var filters = receiver.Definition.Arguments
                .Where(x => SchemaNames.IsFilterInputType(x.Type.NamedType().Name.Value))
                .ToList();

            if (filters.Count != 1)
            {
                this.Report(
                    name,
                    filters.Count == 0
                        ? $"'{field}' declares no filter argument to attach"
                        : $"'{field}' declares more than one filter argument, so '{method}' does not say which one it sets");

                return null;
            }

            argument = filters[0];
        }
        else
        {
            argument = receiver.Definition.Arguments.FirstOrDefault(x =>
                SchemaNames.FieldName(x.Name.Value) == method
                || (method == "Sort" && SchemaNames.FieldName(x.Name.Value) == "Order"));

            if (argument is null)
            {
                this.Report(name, $"'{method}' is not a field of the type being selected, and '{field}' declares no argument by that name");

                return null;
            }
        }

        // A second call for the same argument would write it twice, which the runtime translator rejects too:
        // Order(…) and Sort(…) both name 'order', so the aliased spelling makes this reachable by accident.
        if (receiver.Arguments.Any(x => x.Name.Value == argument.Name.Value))
        {
            this.Report(name, $"'{field}' is given '{argument.Name.Value}' twice; a field can carry each argument once");

            return null;
        }

        if (arguments.Arguments.Count > 1)
        {
            this.Report(arguments, $"'{method}' attaches at most one value");

            return null;
        }

        var expression = arguments.Arguments.FirstOrDefault()?.Expression;

        if (SchemaNames.IsFilterInputType(argument.Type.NamedType().Name.Value))
        {
            // A filter is one value however the schema spells its argument, so a list of filters has no shape
            // the binder can build.
            if (argument.Type.IsListType() || (argument.Type.IsNonNullType() && argument.Type.InnerType().IsListType()))
            {
                this.Report(arguments, $"'{field}' declares its filter as a list, which a declared query cannot build as one value");

                return null;
            }

            if (expression is not LambdaExpressionSyntax lambda)
            {
                this.Report((SyntaxNode?)expression ?? arguments, $"a filter on '{field}' has to be written as a lambda over its builder");

                return null;
            }

            if (this.FilterValue(lambda, argument.Type) is not { } filter)
            {
                return null;
            }

            return new ResolvedField(
                receiver.Definition,
                [.. receiver.Arguments, new ArgumentNode(argument.Name.Value, this.Declare(filter, argument.Type))]);
        }

        // An extension with an optional parameter can be called with nothing, which the runtime path attaches
        // as an explicit null rather than as an omitted argument.
        if (expression is null)
        {
            return new ResolvedField(
                receiver.Definition,
                [.. receiver.Arguments, new ArgumentNode(argument.Name.Value, new NullValueNode(location: null))]);
        }

        return this.ValueOf(expression, argument.Type, values) is { } value
            ? new ResolvedField(receiver.Definition, [.. receiver.Arguments, new ArgumentNode(argument.Name.Value, value)])
            : null;
    }

    /// <remarks>
    /// A filter is one variable holding the whole filter, so the values inside it are not variables of their own:
    /// the binder rebuilds the dictionary from the declaration's parameters when the query is sent.
    /// </remarks>
    private DeclaredQueryFilterValue? FilterValue(LambdaExpressionSyntax lambda, ITypeNode type)
    {
        if (ParameterName(lambda) is not { } parameter || lambda.Body is not ExpressionSyntax body)
        {
            this.Report(lambda, "a filter has to be an expression over the filter's builder, the way f => f.Name(n => n.Eq(\"x\")) is");

            return null;
        }

        var fields = this.FilterFields(body, type.NamedType().Name.Value, parameter);

        if (fields is null)
        {
            return null;
        }

        if (fields.Count == 0)
        {
            this.Report(lambda, "a filter has to set at least one operation; omit the filter to query without one");

            return null;
        }

        return new DeclaredQueryFilterValue(fields);
    }

    private List<DeclaredQueryFilterField>? FilterFields(ExpressionSyntax expression, string filterType, string parameter)
    {
        var chain = Unparenthesised(expression);

        if (IsOnParameter(chain, parameter))
        {
            return [];
        }

        if (chain is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } call)
        {
            this.Report(expression, "a filter is built from the filter builder's methods, the way f.Name(n => n.Eq(\"x\")) is");

            return null;
        }

        var fields = this.FilterFields(access.Expression, filterType, parameter);

        if (fields is null)
        {
            return null;
        }

        var member = access.Name.Identifier.ValueText;
        var field = this.model.FieldsOf(filterType).FirstOrDefault(x => SchemaNames.FieldName(x.Name.Value) == member);

        if (field is null)
        {
            this.Report(access.Name, $"'{member}' is not a filter on '{filterType}'");

            return null;
        }

        if (call.ArgumentList.Arguments.Count != 1)
        {
            this.Report(call, $"'{member}' takes exactly one value");

            return null;
        }

        var fieldType = field.Type.NamedType().Name.Value;
        var value = this.FilterFieldValue(call.ArgumentList.Arguments[0].Expression, field.Type, member);

        if (value is null)
        {
            return null;
        }

        // A list of a filter input type is a combinator, which the runtime translator folds rather than sets.
        var isList = SchemaNames.IsFilterInputType(fieldType)
            && (field.Type.IsListType() || (field.Type.IsNonNullType() && field.Type.InnerType().IsListType()));

        fields.Add(new DeclaredQueryFilterField(field.Name.Value, isList, value));

        return fields;
    }

    private DeclaredQueryBoundValue? FilterFieldValue(ExpressionSyntax expression, ITypeNode type, string member)
    {
        var nestedType = type.NamedType().Name.Value;

        if (!SchemaNames.IsFilterInputType(nestedType))
        {
            return this.FilterLeaf(expression);
        }

        if (expression is not LambdaExpressionSyntax lambda
            || ParameterName(lambda) is not { } parameter
            || lambda.Body is not ExpressionSyntax body)
        {
            this.Report(expression, $"'{member}' builds a nested filter, so it takes a lambda");

            return null;
        }

        var fields = this.FilterFields(body, nestedType, parameter);

        if (fields is null)
        {
            return null;
        }

        if (fields.Count == 0)
        {
            this.Report(expression, $"'{member}' has to set at least one filter");

            return null;
        }

        return new DeclaredQueryFilterValue(fields);
    }

    private DeclaredQueryBoundValue? FilterLeaf(ExpressionSyntax expression)
    {
        if (expression is IdentifierNameSyntax identifier && this.values.Contains(identifier.Identifier.ValueText))
        {
            return new DeclaredQueryParameterValue(identifier.Identifier.ValueText);
        }

        if (!this.declaredValues.TryGetValue(expression.SpanStart, out var value))
        {
            this.ReportValue(
                expression,
                "only a parameter, a constant the compiler can see, or a list of those can be a filter value");

            return null;
        }

        return this.BoundValue(value, expression);
    }

    /// <remarks>
    /// An enum member is bound by name, because that is the form it takes on the wire. An object initialiser is
    /// not rebuildable inside a filter: a filter operation takes a scalar or a list of scalars.
    /// </remarks>
    private DeclaredQueryBoundValue? BoundValue(DeclaredValue value, SyntaxNode location) => value switch
    {
        DeclaredConstant constant => new DeclaredQueryConstantValue(constant.Constant),
        DeclaredEnumMember member => new DeclaredQueryConstantValue(member.Member),
        DeclaredParameter parameter when this.values.Contains(parameter.Name) => new DeclaredQueryParameterValue(parameter.Name),
        DeclaredList list => this.BoundList(list, location),
        _ => this.UnsupportedBound(location, "only a parameter, a constant, an enum member, or a list of those can be a filter value"),
    };

    private DeclaredQueryBoundValue? BoundList(DeclaredList list, SyntaxNode location)
    {
        List<DeclaredQueryBoundValue> items = [];

        foreach (var item in list.Items)
        {
            if (this.BoundValue(item, location) is not { } value)
            {
                return null;
            }

            items.Add(value);
        }

        return new DeclaredQueryListValue(items);
    }

    private DeclaredQueryBoundValue? UnsupportedBound(SyntaxNode node, string reason)
    {
        this.ReportValue(node, reason);

        return null;
    }

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

    private VariableNode Variable(string parameter, ITypeNode type)
        => this.Declare(new DeclaredQueryParameterValue(parameter), type);

    private VariableNode Declare(DeclaredQueryBoundValue value, ITypeNode type)
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

        this.bindings.Add(new DeclaredQueryBinding(name, value));

        return new VariableNode(name);
    }

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

    /// <remarks>
    /// A positional argument fills the schema's argument in the same position, because the generated
    /// <c>Execute*</c> method declares its parameters in schema order and under the schema's own names.
    /// </remarks>
    private static InputValueDefinitionNode? DeclaredArgument(FieldDefinitionNode field, ArgumentSyntax argument, int position)
        => argument.NameColon is { } named
            ? field.Arguments.FirstOrDefault(x => x.Name.Value == named.Name.Identifier.ValueText)
            : position < field.Arguments.Count ? field.Arguments[position] : null;

    private IReadOnlyList<DeclaredQueryBinding> Bindings() => [.. this.bindings];

    private static bool IsOnParameter(ExpressionSyntax expression, string parameter)
        => Unparenthesised(expression) is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter;

    private static ExpressionSyntax Unparenthesised(ExpressionSyntax expression)
        => expression is ParenthesizedExpressionSyntax parenthesized ? Unparenthesised(parenthesized.Expression) : expression;

    /// <remarks>
    /// Outermost first, because that is the cast whose fragment wraps the whole selection, and never inside a lambda:
    /// a cast in a projection belongs to that projection's own selection.
    /// </remarks>
    private static (SyntaxNode Node, ExpressionSyntax Operand, string? Type)? CastOf(ExpressionSyntax expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node != expression && !IsReceiver(node, expression))
            {
                continue;
            }

            if (node is CastExpressionSyntax cast)
            {
                return (cast, Unparenthesised(cast.Expression), TypeName(cast.Type));
            }

            if (node is BinaryExpressionSyntax binary
                && binary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsExpression)
                && binary.Right is TypeSyntax right)
            {
                return (binary, Unparenthesised(binary.Left), TypeName(right));
            }
        }

        return null;
    }

    private static InvocationExpressionSyntax? OfTypeOf(ExpressionSyntax expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation
                && access.Name.Identifier.ValueText == nameof(Enumerable.OfType)
                && !WithinLambda(node, expression))
            {
                return invocation;
            }
        }

        return null;
    }

    private static bool IsReceiver(SyntaxNode node, SyntaxNode expression)
    {
        var current = node;

        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current.Parent is MemberAccessExpressionSyntax access
            && access.Expression == current
            && !WithinLambda(current, expression);
    }

    private static bool WithinLambda(SyntaxNode node, SyntaxNode expression)
    {
        if (node == expression)
        {
            return false;
        }

        for (var ancestor = node.Parent; ancestor is not null && ancestor != expression; ancestor = ancestor.Parent)
        {
            if (ancestor is AnonymousFunctionExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the identifier an expression is built on, so a value used through a member or a call is still
    /// recognisable as that value.
    /// </summary>
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
    private sealed record ResolvedField(FieldDefinitionNode Definition, IReadOnlyList<ArgumentNode> Arguments);
}
