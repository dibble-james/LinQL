namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using HotChocolate.Language.Visitors;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

internal class DocumentWalker : SyntaxWalker<DocumentWalkerContext>
{
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

    private readonly HashSet<string> inputObjectTypes = new(StringComparer.Ordinal);

    protected override ISyntaxVisitorAction VisitChildren(DocumentNode node, DocumentWalkerContext context)
    {
        var action = base.VisitChildren(node, context);

        // The filter input types may be declared after the fields that use them, so the check runs
        // once the whole document has been walked.
        this.ReportMissingFilterInputTypes(node, context);

        return action;
    }

    protected override ISyntaxVisitorAction VisitChildren(OperationTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.RootOperations.Add(
            new RootTypeClass(
                node.Type.Name.Value,
                node.Operation));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        var rootNode = context.RootOperations.FirstOrDefault(x => x.Name == node.Name.Value);

        if (rootNode is not null)
        {
            rootNode.WithFields(node.Fields);
        }
        else
        {
            context.Types.Add(new ComplexTypeClass(node.Name.Value, node.Fields, node.Interfaces.Select(x => x.Name.Value)));
        }

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InterfaceTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Types.Add(new InterfaceTypeClass(node.Name.Value, node.Fields, node.Interfaces.Select(x => x.Name.Value)));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(UnionTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Types.Add(new InterfaceTypeClass(node.Name.Value, [], []));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(EnumTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Types.Add(new EnumTypeClass(node.Name.Value, node.Values));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InputObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        this.inputObjectTypes.Add(node.Name.Value);

        var fields = node.Fields.Select(x => new FieldDefinitionNode(x.Location, x.Name, x.Description, [], x.Type, x.Directives));

        context.Types.Add(new ComplexTypeClass(node.Name.Value, fields, []));

        if (ComplexTypeClass.IsFilterInputType(node.Name.Value))
        {
            context.Types.Add(new FilterInputBuilderClass(node.Name.Value, [.. fields]));
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
            context.Context.ReportDiagnostic(
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
            context.Context.ReportDiagnostic(
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
            context.Scalars.Add(new Scalar(node.Name.Value, clrType.Value!.Value!.ToString(), primitiveType.Value!.Value!.ToString()));
        }
        else
        {
            context.Scalars.Add(new Scalar(node.Name.Value, clrType.Value!.Value!.ToString()));
        }

        return base.VisitChildren(node, context);
    }

    private void ReportMissingFilterInputTypes(DocumentNode document, DocumentWalkerContext context)
    {
        foreach (var definition in document.Definitions)
        {
            var fields = definition switch
            {
                ObjectTypeDefinitionNode objectType => objectType.Fields,
                InterfaceTypeDefinitionNode interfaceType => interfaceType.Fields,
                _ => null,
            };

            if (fields is null)
            {
                continue;
            }

            foreach (var field in fields)
            {
                foreach (var argument in field.Arguments.Where(x => ComplexTypeClass.IsFilterInputType(x.Type.NamedType().Name.Value)))
                {
                    var typeName = argument.Type.NamedType().Name.Value;

                    if (this.inputObjectTypes.Contains(typeName))
                    {
                        continue;
                    }

                    context.Context.ReportDiagnostic(
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
}
