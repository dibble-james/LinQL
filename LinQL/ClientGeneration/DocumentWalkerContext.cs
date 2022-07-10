namespace LinQL.ClientGeneration;

using HotChocolate.Language.Visitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext : ISyntaxVisitorContext
{
    private readonly string graphNamespace;
    private readonly string[] extraUsings;

    public DocumentWalkerContext(string graphName, string graphNamespace, string[] extraUsings)
    {
        this.Graph = new GraphClass(graphName);
        this.graphNamespace = graphNamespace;
        this.extraUsings = extraUsings;
    }

    public GraphClass Graph { get; }

    public override string ToString()
    {
        var ns = NamespaceDeclaration(IdentifierName(this.graphNamespace))
            .AddUsings(
                UsingDirective(IdentifierName("LinQL")),
                UsingDirective(IdentifierName("LinQL.Description")),
                UsingDirective(IdentifierName("LinQL.Translation")),
                UsingDirective(IdentifierName("Microsoft.Extensions.Logging")),
                UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")));

        if (this.extraUsings.Any())
        {
            ns = ns.AddUsings(this.extraUsings.Select(x => UsingDirective(IdentifierName(x))).ToArray());
        }

        ns = ns.AddMembers(new ServiceCollectionExtenionsClass(this.Graph.Name).Create())
          .AddMembers(this.Graph.Create())
          .AddMembers(this.Graph.RootOperations.Select(x => x.Create()).ToArray())
          .AddMembers(this.Graph.Types.Select(x => x.Create()).ToArray());

        using var clientContent = new StringWriter();
        clientContent.WriteLine("#nullable enable");
        clientContent.WriteLine("#pragma warning disable CS8618");
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
