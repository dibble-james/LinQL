namespace LinQL.ClientGeneration;

using HotChocolate.Language.Visitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext : ISyntaxVisitorContext
{
    private readonly string graphNamespace;

    public DocumentWalkerContext(string graphName, string graphNamespace)
    {
        this.Graph = new GraphClass(graphName);
        this.graphNamespace = graphNamespace;
    }

    public GraphClass Graph { get; }

    public override string ToString()
    {
        var ns = NamespaceDeclaration(IdentifierName(this.graphNamespace))
            .AddMembers(this.Graph.Create())
            .AddMembers(this.Graph.RootOperations.Select(x => x.Create()).ToArray())
            .AddMembers(this.Graph.Types.Select(x => x.Create()).ToArray());

        using var clientContent = new StringWriter();
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
