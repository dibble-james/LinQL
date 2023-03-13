namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language.Visitors;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext : ISyntaxVisitorContext
{
    private readonly string graphNamespace;
    private readonly string[] extraUsings;

    public DocumentWalkerContext(string file, SourceProductionContext context, string graphNamespace, string[] extraUsings)
    {
        this.File = file;
        this.Context = context;
        this.graphNamespace = graphNamespace;
        this.extraUsings = extraUsings;
    }

    public List<RootTypeClass> RootOperations { get; } = new();

    public List<IClassFactory> Types { get; } = new();

    public List<Scalar> Scalars { get; } = Scalar.NativeScalars.ToList();

    public string File { get; }

    public SourceProductionContext Context { get; }

    public override string ToString()
    {
        var ns = NamespaceDeclaration(IdentifierName(this.graphNamespace))
            .AddUsings(
                UsingDirective(IdentifierName("System.Text.Json")),
                UsingDirective(IdentifierName("LinQL")),
                UsingDirective(IdentifierName("LinQL.Description")),
                UsingDirective(IdentifierName("LinQL.Translation")),
                UsingDirective(IdentifierName("Microsoft.Extensions.Logging")),
                UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")));

        if (this.extraUsings.Any())
        {
            ns = ns.AddUsings(this.extraUsings.Select(x => UsingDirective(IdentifierName(x))).ToArray());
        }

        var scalars = this.Scalars.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);

        ns = ns.AddMembers(this.RootOperations.Select(x => x.Create(scalars)).ToArray())
          .AddMembers(this.Types.Select(x => x.Create(scalars)).ToArray())
          .AddMembers(new OptionExtensionsClass().Create(scalars))
          .AddMembers(new InterfaceRegistrationExtensionsClass(this.Types.OfType<ComplexTypeClass>()).Create(scalars));

        using var clientContent = new StringWriter();
        clientContent.WriteLine("#nullable enable");
        clientContent.WriteLine("#pragma warning disable CS8618");
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
