namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using HotChocolate.Language.Visitors;

internal class DocumentWalker : SyntaxWalker<DocumentWalkerContext>
{
    protected override ISyntaxVisitorAction VisitChildren(OperationTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.RootOperations.Add(
            new RootTypeClass(
                node.Type.Name.Value,
                node.Operation));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        var rootNode = context.Graph.RootOperations.FirstOrDefault(x => x.Name == node.Name.Value);

        if (rootNode is not null)
        {
            rootNode.WithFields(node.Fields);
        }
        else
        {
            context.Graph.Types.Add(new ComplexTypeClass(node.Name.Value, node.Fields, node.Interfaces.Select(x => x.Name.Value)));
        }

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InterfaceTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.Types.Add(new InterfaceTypeClass(node.Name.Value, node.Fields, node.Interfaces.Select(x => x.Name.Value)));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(UnionTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.Types.Add(new InterfaceTypeClass(node.Name.Value, Enumerable.Empty<FieldDefinitionNode>(), Enumerable.Empty<string>()));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(EnumTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.Types.Add(new EnumTypeClass(node.Name.Value, node.Values));

        return base.VisitChildren(node, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(InputObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        var fields = node.Fields.Select(x => new FieldDefinitionNode(x.Location, x.Name, x.Description, new List<InputValueDefinitionNode>(), x.Type, x.Directives));

        context.Graph.Types.Add(new ComplexTypeClass(node.Name.Value, fields, Enumerable.Empty<string>()));

        return base.VisitChildren(node, context);
    }
}
