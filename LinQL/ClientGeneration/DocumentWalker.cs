namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using HotChocolate.Language.Visitors;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

internal class DocumentWalker : SyntaxWalker<DocumentWalkerContext>
{
    private const string EntityKeyDirective = "key";

    private const string EntityKeyFieldsArgument = "fields";

    private static readonly DiagnosticDescriptor MissingDirective = new(
        id: "LINQLGEN03",
        title: "Missing scalar directive",
        messageFormat: "@customScalar directive missing on type {0}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingClrType = new(
        id: "LINQLGEN04",
        title: "Missing CLR type on scalar directive",
        messageFormat: "clrType argument for the @customScalar directive on type {0}",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingFilterInputType = new(
        id: "LINQLGEN05",
        title: "Missing filter input type",
        messageFormat: "Filter argument '{0}' on '{1}' has type '{2}', but the SDL does not define that as an input object. Export the SDL from a server with filtering configured so the filter input types are included.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingFilterArgument = new(
        id: "LINQLGEN06",
        title: "Conflicting filter argument names",
        messageFormat: "Filter argument '{0}' on '{1}' conflicts with '{2}' on '{3}': both fields return '{4}', so the generated Filter extension can only attach one argument name. Rename the arguments to match, or write the extension for one of the fields by hand.",
        category: "LinQLClientGenerator",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    protected override ISyntaxVisitorAction VisitChildren(DocumentNode node, DocumentWalkerContext context)
    {
        var action = base.VisitChildren(node, context);

        // The filter input types may be declared after the fields that use them, so the check runs
        // once the whole document has been walked.
        ReportMissingFilterInputTypes(context);
        ReportConflictingFilterExtensions(context);
        ResolveEntityKeys(context);

        return action;
    }

    protected override ISyntaxVisitorAction VisitChildren(OperationTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Model.AddRootOperation(
            new RootTypeClass(
                node.Type.Name.Value,
                node.Operation,
                context.TypesAs));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Model.AddDeclaredType(node.Name.Value, DeclaredTypeKind.Object, node.Fields);
        CollectFieldExtensions(node.Fields, context);

        // A root operation type is emitted as a RootTypeClass instead, but it declares the same
        // fields, so the model holds them either way.
        if (!context.Model.RootOperations.Any(x => x.Name == node.Name.Value))
        {
            context.Model.AddType(new ComplexTypeClass(node.Name.Value, node.Interfaces.Select(x => x.Name.Value), context.TypesAs));
        }

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ObjectTypeExtensionNode node, DocumentWalkerContext context)
    {
        // The @key annotations live in the schema's .extensions.graphql companion, where they extend the
        // types the schema declares. Nothing else an object extension carries is read: an extension that
        // adds fields is not part of the model today, and this must not start adding them.
        CollectEntityKeyPatterns(node.Directives, node.Name.Value, context);

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(SchemaExtensionNode node, DocumentWalkerContext context)
    {
        // A key on the schema is the global pattern: it applies to every object type that declares the field.
        CollectEntityKeyPatterns(node.Directives, typeName: null, context);

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InterfaceTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Model.AddDeclaredType(node.Name.Value, DeclaredTypeKind.Interface, node.Fields);
        CollectFieldExtensions(node.Fields, context);

        context.Model.AddType(new InterfaceTypeClass(node.Name.Value, node.Interfaces.Select(x => x.Name.Value), context.TypesAs));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(UnionTypeDefinitionNode node, DocumentWalkerContext context)
    {
        // A union has no fields of its own: its selection is fragments only, so Includes has nothing to
        // add and the marker is left off to reject it at compile time.
        context.Model.AddType(new InterfaceTypeClass(node.Name.Value, [], context.TypesAs, canHaveIncludes: false));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(EnumTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Model.AddType(new EnumTypeClass(node.Name.Value, node.Values));
        context.Model.AddEnumType(node.Name.Value);

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InputObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        List<FieldDefinitionNode> fields = [.. node.Fields.Select(x => new FieldDefinitionNode(x.Location, x.Name, x.Description, [], x.Type, x.Directives))];

        context.Model.AddDeclaredType(node.Name.Value, DeclaredTypeKind.InputObject, fields);
        context.Model.AddType(new ComplexTypeClass(node.Name.Value, [], context.TypesAs, canHaveIncludes: false));

        if (SchemaNames.IsFilterInputType(node.Name.Value))
        {
            context.Model.AddType(new FilterInputBuilderClass(node.Name.Value));
        }

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ScalarTypeExtensionNode node, DocumentWalkerContext context)
    {
        var scalarOptions = node.Directives.FirstOrDefault(x => x.Name.Value == "customScalar");
        var clrType = scalarOptions?.Arguments.FirstOrDefault(x => x.Name.Value == "clrType");
        var primitiveType = scalarOptions?.Arguments.FirstOrDefault(x => x.Name.Value == "primitiveType");

        if (scalarOptions is null)
        {
            context.Report(
                Diagnostic.Create(
                    MissingDirective,
                    Microsoft.CodeAnalysis.Location.Create(
                        context.File,
                        TextSpan.FromBounds(node.Location!.Start, node.Location!.End),
                        new LinePositionSpan(new LinePosition(node.Location!.Line, node.Location!.Start), new LinePosition(node.Location!.Line, node.Location!.End))),
                    node.Name.Value));

            return base.VisitChildren(node, context);
        }

        if (clrType is null)
        {
            context.Report(
                Diagnostic.Create(
                    MissingClrType,
                    Microsoft.CodeAnalysis.Location.Create(
                        context.File,
                        TextSpan.FromBounds(node.Location!.Start, node.Location!.End),
                        new LinePositionSpan(new LinePosition(node.Location!.Line, node.Location!.Start), new LinePosition(node.Location!.Line, node.Location!.End))),
                    node.Name.Value));

            return base.VisitChildren(node, context);
        }

        if (primitiveType is not null)
        {
            context.Model.AddScalar(new Scalar(node.Name.Value, clrType.Value!.Value!.ToString(), primitiveType.Value!.Value!.ToString()));
        }
        else
        {
            context.Model.AddScalar(new Scalar(node.Name.Value, clrType.Value!.Value!.ToString()));
        }

        return base.VisitChildren(node, context);
    }

    /// <summary>
    /// Reports a field whose filter argument name is ignored, because another field returning the same
    /// type was seen first.
    /// </summary>
    /// <remarks>
    /// An extension only knows the type it extends, so the generated <c>Filter</c> method takes the
    /// argument name and filter input type of the first field in document order. A second field returning
    /// the same type with a different argument name cannot be expressed by that method, and silently
    /// filtering through the wrong argument would produce a document the server rejects.
    /// </remarks>
    private static void ReportConflictingFilterExtensions(DocumentWalkerContext context)
    {
        var chosen = new Dictionary<string, (string Field, string Argument)>(StringComparer.Ordinal);

        foreach (var type in context.Model.DeclaredTypes.Where(x => x.IsSelectable))
        {
            foreach (var field in type.Fields)
            {
                var filterArgument = field.Arguments.FirstOrDefault(x => SchemaNames.IsFilterInputType(x.Type.NamedType().Name.Value));

                if (filterArgument is null)
                {
                    continue;
                }

                var receiver = context.Model.TypeName(field.Type);
                var key = $"{receiver}|{filterArgument.Type.NamedType().Name.Value}";

                if (!chosen.TryGetValue(key, out var first))
                {
                    chosen[key] = (Field: field.Name.Value, Argument: filterArgument.Name.Value);
                    continue;
                }

                if (first.Argument == filterArgument.Name.Value)
                {
                    continue;
                }

                context.Report(
                    Diagnostic.Create(
                        ConflictingFilterArgument,
                        Microsoft.CodeAnalysis.Location.Create(
                            context.File,
                            TextSpan.FromBounds(filterArgument.Location!.Start, filterArgument.Location!.End),
                            new LinePositionSpan(
                                new LinePosition(filterArgument.Location!.Line - 1, filterArgument.Location!.Column - 1),
                                new LinePosition(filterArgument.Location!.Line - 1, filterArgument.Location!.Column - 1))),
                        filterArgument.Name.Value,
                        field.Name.Value,
                        first.Argument,
                        first.Field,
                        receiver));
            }
        }
    }

    /// <summary>
    /// Records the field arguments that get an extension method. Only a list of arguments is kept: the
    /// types are rendered once the whole document has been walked and the scalars are known.
    /// </summary>
    /// <remarks>
    /// Only filterable fields take part. The extensions exist so a filter composes with the field's other
    /// arguments (<c>q.Featured.First(2).Filter(…)</c>); a field with no filter argument has no reason to
    /// grow an extension per argument.
    /// </remarks>
    private static void CollectFieldExtensions(IReadOnlyList<FieldDefinitionNode> fields, DocumentWalkerContext context)
    {
        foreach (var field in fields.Where(x => x.Arguments.Any(a => SchemaNames.IsFilterInputType(a.Type.NamedType().Name.Value))))
        {
            foreach (var argument in field.Arguments)
            {
                context.Model.AddFieldExtension(new FieldExtension(field, argument, SchemaNames.IsFilterInputType(argument.Type.NamedType().Name.Value)));
            }
        }
    }

    private static void ReportMissingFilterInputTypes(DocumentWalkerContext context)
    {
        foreach (var type in context.Model.DeclaredTypes.Where(x => x.IsSelectable))
        {
            foreach (var field in type.Fields)
            {
                foreach (var argument in field.Arguments.Where(x => SchemaNames.IsFilterInputType(x.Type.NamedType().Name.Value)))
                {
                    var typeName = argument.Type.NamedType().Name.Value;

                    if (context.Model.HasInputObjectType(typeName))
                    {
                        continue;
                    }

                    context.Report(
                        Diagnostic.Create(
                            MissingFilterInputType,
                            Microsoft.CodeAnalysis.Location.Create(
                                context.File,
                                TextSpan.FromBounds(argument.Location!.Start, argument.Location!.End),
                                new LinePositionSpan(
                                    new LinePosition(argument.Location!.Line - 1, argument.Location!.Column - 1),
                                    new LinePosition(argument.Location!.Line - 1, argument.Location!.Column - 1))),
                            argument.Name.Value,
                            field.Name.Value,
                            typeName));
                }
            }
        }
    }

    /// <remarks>
    /// The pattern is recorded as written. Whether it names a usable field is decided once the whole document
    /// has been read, because the type it extends may be declared after its extension.
    /// </remarks>
    private static void CollectEntityKeyPatterns(
        IReadOnlyList<DirectiveNode> directives,
        string? typeName,
        DocumentWalkerContext context)
    {
        foreach (var directive in directives.Where(x => x.Name.Value == EntityKeyDirective))
        {
            var fields = directive.Arguments.FirstOrDefault(x => x.Name.Value == EntityKeyFieldsArgument)?.Value;

            context.Model.AddEntityKeyPattern(new EntityKeyPattern(
                typeName,
                fields is StringValueNode literal
                    ? literal.Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    : [],
                directive.Location));
        }
    }

    /// <remarks>
    /// A global pattern applies wherever its field exists, which is how Strawberry Shake applies one: a type
    /// that does not declare the field is simply not an entity, and that is not a diagnostic. A per-type
    /// pattern is explicit, so it is reported when it cannot be used and the type is left uncached rather than
    /// silently falling back to the global key the author replaced.
    /// Root operation types and interfaces are never entities.
    /// </remarks>
    private static void ResolveEntityKeys(DocumentWalkerContext context)
    {
        var model = context.Model;
        var global = model.EntityKeyPatterns.FirstOrDefault(x => x.TypeName is null);

        if (global is not null && global.Fields.Count != 1)
        {
            ReportEntityKey(context, EntityKeyDiagnostics.MultipleFields, "the schema", global.Fields, global.Location);
        }

        var overrides = new Dictionary<string, EntityKeyPattern>(StringComparer.Ordinal);

        foreach (var pattern in model.EntityKeyPatterns)
        {
            if (pattern.TypeName is { } name)
            {
                // The first pattern for a type wins: an entity has one key, and a second annotation cannot
                // be expressed by the generated code.
                if (!overrides.ContainsKey(name))
                {
                    overrides[name] = pattern;
                }
            }
        }

        var roots = new HashSet<string>(model.RootOperations.Select(x => x.Name), StringComparer.Ordinal);

        foreach (var type in model.DeclaredTypes.Where(x => x.Kind is DeclaredTypeKind.Object && !roots.Contains(x.Name)))
        {
            var declared = overrides.TryGetValue(type.Name, out var found) ? found : null;
            var pattern = declared ?? global;

            if (pattern is null)
            {
                continue;
            }

            if (pattern.Fields.Count != 1)
            {
                if (declared is not null)
                {
                    ReportEntityKey(context, EntityKeyDiagnostics.MultipleFields, $"'{type.Name}'", pattern.Fields, pattern.Location);
                }

                continue;
            }

            var fieldName = pattern.Fields[0];
            var field = type.Fields.FirstOrDefault(x => x.Name.Value == fieldName);

            if (field is null)
            {
                if (declared is not null)
                {
                    ReportEntityKey(context, EntityKeyDiagnostics.UnknownField, type.Name, pattern.Fields, pattern.Location);
                }

                continue;
            }

            if (!model.IsLeafType(field.Type.NamedType().Name.Value))
            {
                if (declared is not null)
                {
                    ReportEntityKey(context, EntityKeyDiagnostics.NotALeafField, type.Name, pattern.Fields, pattern.Location);
                }

                continue;
            }

            model.AddEntityKey(new EntityKey(type.Name, fieldName));
        }
    }

    private static void ReportEntityKey(
        DocumentWalkerContext context,
        DiagnosticDescriptor descriptor,
        string target,
        IReadOnlyList<string> fields,
        HotChocolate.Language.Location? location)
    {
        var span = location is null
            ? Microsoft.CodeAnalysis.Location.None
            : Microsoft.CodeAnalysis.Location.Create(
                context.File,
                TextSpan.FromBounds(location.Start, location.End),
                new LinePositionSpan(
                    new LinePosition(location.Line - 1, location.Column - 1),
                    new LinePosition(location.Line - 1, location.Column - 1)));

        context.Report(Diagnostic.Create(descriptor, span, target, string.Join(" ", fields)));
    }
}
