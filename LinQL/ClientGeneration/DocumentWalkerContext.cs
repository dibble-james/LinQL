namespace LinQL.ClientGeneration;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

internal class DocumentWalkerContext(string file, SourceProductionContext context, string graphNamespace, string[] extraUsings, LinQLTypesAs typesAs)
{
    /// <summary>
    /// Gets the schema the walk builds, which the emitter renders.
    /// </summary>
    public SchemaModel Model { get; } = new();

    public string File { get; } = file;

    public LinQLTypesAs TypesAs { get; } = typesAs;

    public SourceProductionContext Context { get; } = context;

    /// <summary>
    /// Gets or sets a value indicating whether the walk reports diagnostics.
    /// </summary>
    /// <remarks>
    /// A declared query needs a schema to read, not another copy of the diagnostics the client's own walk
    /// already reported, so building its model walks with this off.
    /// </remarks>
    public bool ReportsDiagnostics { get; set; } = true;

    /// <summary>
    /// Reports a diagnostic, unless the walk is only building the model.
    /// </summary>
    /// <param name="diagnostic">The diagnostic.</param>
    public void Report(Diagnostic diagnostic)
    {
        if (this.ReportsDiagnostics)
        {
            this.Context.ReportDiagnostic(diagnostic);
        }
    }

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

        var model = this.Model;

        ns = ns.AddMembers([.. model.RootOperations.Select(x => x.Create(model))])
          .AddMembers([.. model.Types.Select(x => x.Create(model))])
          .AddMembers(new OptionExtensionsClass().Create(model));

        if (model.FieldExtensions.Any())
        {
            ns = ns.AddMembers(new FieldExtensionsClass().Create(model));
        }

        ns = ns.AddMembers(new InterfaceRegistrationExtensionsClass().Create(model));

        using var clientContent = new StringWriter();
        clientContent.WriteLine("#nullable enable");
        clientContent.WriteLine("#pragma warning disable CS8618");
        ns.NormalizeWhitespace().WriteTo(clientContent);

        return clientContent.ToString();
    }
}
