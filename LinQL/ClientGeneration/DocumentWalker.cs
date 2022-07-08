namespace LinQL.ClientGeneration;

using HotChocolate.Language;
using HotChocolate.Language.Visitors;

internal class DocumentWalker : SyntaxWalker<DocumentWalkerContext>
{
    protected override ISyntaxVisitorAction VisitChildren(OperationTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.RootOperations.Add(new RootTypeClass(node.Type.Name.Value));

        return base.VisitChildren(node.Type, context);
    }

    protected override ISyntaxVisitorAction VisitChildren(ObjectTypeDefinitionNode node, DocumentWalkerContext context)
    {
        context.Graph.Types.Add(new ComplexTypeClass(node.Name.Value, node.Fields));

        return base.VisitChildren(node, context);
    }
}
