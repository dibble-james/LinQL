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
/// writes <c>...on Film</c> into <c>result</c>'s selection set. A cast and an <c>OfType</c> write the same fragment by a
/// shorter route — <c>((Film)q.Result).Title</c> and <c>q.Results.OfType&lt;Film&gt;().Select(f => f.Title)</c> — because on the
/// wire both are a type condition over the field's selection.
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
    /// Adds the fragment a cast writes to the selection it is applied to.
    /// </summary>
    /// <param name="expression">The whole selection expression, which carries the cast.</param>
    /// <param name="cast">The cast, the field it is applied to, and the type it casts to.</param>
    /// <param name="typeName">The type the enclosing selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
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

    /// <summary>
    /// Adds the selections a fragment carries.
    /// </summary>
    /// <param name="inner">The selections, or <see langword="null"/> when the fragment selects the type's scalars.</param>
    /// <param name="typeName">The type the fragment is on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The fragment's selection set being built.</param>
    private void AddFragmentSelections(ExpressionSyntax? inner, string typeName, string parameter, IReadOnlyList<string> values, List<ISelectionNode> selections)
    {
        if (inner is null)
        {
            selections.AddRange(this.ScalarFieldsOf(typeName).Select(x => Field(x, selectionSet: null)));

            return;
        }

        this.AddSelections(inner, typeName, parameter, values, selections);
    }

    /// <summary>
    /// Adds the fragment an <c>OfType</c> writes to the field it filters.
    /// </summary>
    /// <param name="expression">The whole selection expression, which carries the call.</param>
    /// <param name="ofType">The <c>OfType</c> call.</param>
    /// <param name="typeName">The type the enclosing selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
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

    /// <summary>
    /// Writes an inline fragment of the given type.
    /// </summary>
    /// <param name="typeName">The type the fragment is on, or <see langword="null"/> when the syntax does not name one.</param>
    /// <param name="location">Where to report a type the document cannot name.</param>
    /// <param name="children">Adds the fragment's selections, given the fragment's type.</param>
    /// <returns>The fragment, or <see langword="null"/> when the document cannot write it.</returns>
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

    /// <summary>
    /// Adds the fragments a spread writes to the selection it is called on.
    /// </summary>
    /// <param name="invocation">The outermost <c>On</c> call.</param>
    /// <param name="typeName">The type the enclosing selection is made on.</param>
    /// <param name="parameter">The lambda parameter that type is bound to.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <param name="selections">The selection set being built.</param>
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

        var childType = field.Definition.Type.NamedType().Name.Value;
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

    /// <summary>
    /// Writes one spread as an inline fragment.
    /// </summary>
    /// <param name="spread">The spread's lambda.</param>
    /// <param name="values">The lambda parameters that become variables.</param>
    /// <returns>The fragment, or <see langword="null"/> when the spread cannot be written.</returns>
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
    /// <param name="invocation">The outermost <c>On</c> call.</param>
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

    /// <summary>
    /// Reads the type a spread's lambda is written for.
    /// </summary>
    /// <param name="lambda">The spread.</param>
    /// <returns>The type's name, or <see langword="null"/> when the parameter does not name one.</returns>
    private static string? SpreadTypeName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => TypeName(simple.Parameter.Type),
        ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 1 } parenthesized
            => TypeName(parenthesized.ParameterList.Parameters[0].Type),
        _ => null,
    };

    /// <summary>
    /// Reads the name a type syntax declares.
    /// </summary>
    /// <param name="type">The type syntax.</param>
    /// <returns>The name, or <see langword="null"/> when it is not one the schema could declare.</returns>
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
        => Unparenthesised(expression) is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == parameter;

    /// <summary>
    /// Removes the parentheses around an expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The expression inside them, which is the same one when there are none.</returns>
    private static ExpressionSyntax Unparenthesised(ExpressionSyntax expression)
        => expression is ParenthesizedExpressionSyntax parenthesized ? Unparenthesised(parenthesized.Expression) : expression;

    /// <summary>
    /// Finds the cast a selection is applied through.
    /// </summary>
    /// <param name="expression">The selection expression.</param>
    /// <returns>
    /// The cast node, the expression it is applied to and the type it casts to, or <see langword="null"/> when the
    /// selection has no cast in it.
    /// </returns>
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

    /// <summary>
    /// Finds the <c>OfType</c> call a selection is applied through.
    /// </summary>
    /// <param name="expression">The selection expression.</param>
    /// <returns>The call, or <see langword="null"/> when the selection has none.</returns>
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

    /// <summary>
    /// Gets whether a node is what a member is selected from.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The selection expression it belongs to.</param>
    /// <returns><see langword="true"/> when the node is the receiver of a member access outside any lambda.</returns>
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

    /// <summary>
    /// Gets whether a node is inside a lambda in the expression being read.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="expression">The expression the node belongs to.</param>
    /// <returns><see langword="true"/> when a lambda stands between the node and the expression.</returns>
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
