namespace LinQL.Tests.Documents;

using FluentAssertions;
using HotChocolate.Language;
using LinQL.Documents;
using Xunit;

public class DocumentRendererTests
{
    [Fact]
    public void RendersListTypeReferences()
    {
        // The generator emits list and nested non-null type references on arguments, which the
        // runtime reads back from the GQLType string before it can build the variable definition.
        var operation = new OperationDefinitionNode(
            location: null,
            name: new NameNode("linql"),
            description: null,
            operation: OperationType.Query,
            variableDefinitions:
            [
                new VariableDefinitionNode(
                    location: null,
                    variable: new VariableNode("var1"),
                    description: null,
                    type: TypeReferenceParser.Parse("[Film!]!"),
                    defaultValue: null,
                    directives: []),
            ],
            directives: [],
            selectionSet: new SelectionSetNode(new List<ISelectionNode>
            {
                new FieldNode(
                    new NameNode("films"),
                    alias: null,
                    directives: [],
                    arguments: [new ArgumentNode("ids", new VariableNode("var1"))],
                    selectionSet: null),
            }));

        DocumentRenderer.Render(operation)
            .Should().Be("query linql(\n  $var1: [Film!]!\n)\n {\n  films(\n    ids: $var1\n  )\n}\n");
    }

    [Fact]
    public void RejectsMalformedTypeReferences()
    {
        var act = () => TypeReferenceParser.Parse("[Film!");

        act.Should().Throw<FormatException>();
    }
}
