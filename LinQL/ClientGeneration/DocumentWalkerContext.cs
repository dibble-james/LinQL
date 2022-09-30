namespace LinQL.ClientGeneration;

using HotChocolate.Language.Visitors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext : ISyntaxVisitorContext
{
    private readonly string graphNamespace;
    private readonly string[] extraUsings;

    public DocumentWalkerContext(string graphNamespace, string[] extraUsings)
    {
        this.graphNamespace = graphNamespace;
        this.extraUsings = extraUsings;
    }

    public List<RootTypeClass> RootOperations { get; } = new();

    public List<IClassFactory> Types { get; } = new();

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

        ns = ns.AddMembers(this.RootOperations.Select(x => x.Create()).ToArray())
          .AddMembers(this.Types.Select(x => x.Create()).ToArray());

        using var clientContent = new StringWriter();
        clientContent.WriteLine("#nullable enable");
        clientContent.WriteLine("#pragma warning disable CS8618");
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
