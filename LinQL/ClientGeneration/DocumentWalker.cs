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

    protected override ISyntaxVisitorAction VisitChildren(EnumTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.Types.Add(new EnumTypeClass(node.Name.Value, node.Values));

        return base.VisitChildren(node, context);
    }
}
