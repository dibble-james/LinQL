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
        context.Types.Add(new InterfaceTypeClass(node.Name.Value, Enumerable.Empty<FieldDefinitionNode>(), Enumerable.Empty<string>()));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(EnumTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Types.Add(new EnumTypeClass(node.Name.Value, node.Values));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InputObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        var fields = node.Fields.Select(x => new FieldDefinitionNode(x.Location, x.Name, x.Description, new List<InputValueDefinitionNode>(), x.Type, x.Directives));

        context.Types.Add(new ComplexTypeClass(node.Name.Value, fields, Enumerable.Empty<string>()));

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
}
