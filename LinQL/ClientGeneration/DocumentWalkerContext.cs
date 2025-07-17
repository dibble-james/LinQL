namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language.Visitors;
using LinQL.Description;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext(string file, SourceProductionContext context, string graphNamespace, string[] extraUsings) : ISyntaxVisitorContext
{
    public List<RootTypeClass> RootOperations { get; } = [];

    public List<IClassFactory> Types { get; } = [];

    public List<Scalar> Scalars { get; } = [.. Scalar.NativeScalars];

    public string File { get; } = file;

    public SourceProductionContext Context { get; } = context;

    public override string ToString()
    {
        var ns = NamespaceDeclaration(IdentifierName(graphNamespace))
            .AddUsings(
                UsingDirective(IdentifierName("System.Text.Json")),
                UsingDirective(IdentifierName("LinQL")),
                UsingDirective(IdentifierName("LinQL.Description")),
                UsingDirective(IdentifierName("LinQL.Translation")),
                UsingDirective(IdentifierName("Microsoft.Extensions.Logging")),
                UsingDirective(IdentifierName("Microsoft.Extensions.DependencyInjection")));

        if (extraUsings.Any())
        {
            ns = ns.AddUsings([.. extraUsings.Select(x => UsingDirective(IdentifierName(x)))]);
        }

        var scalars = this.Scalars.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);

        ns = ns.AddMembers([.. this.RootOperations.Select(x => x.Create(scalars))])
          .AddMembers([.. this.Types.Select(x => x.Create(scalars))])
          .AddMembers(new OptionExtensionsClass().Create(scalars))
          .AddMembers(new InterfaceRegistrationExtensionsClass(this.Types.OfType<ComplexTypeClass>()).Create(scalars));

        using var clientContent = new StringWriter();
        clientContent.WriteLine("#nullable enable");
        clientContent.WriteLine("#pragma warning disable CS8618");
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
