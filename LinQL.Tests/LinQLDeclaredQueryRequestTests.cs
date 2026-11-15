namespace LinQL.Tests;

using System.Collections.Generic;

public class LinQLDeclaredQueryRequestTests
{
    [Fact]
    public void BindCarriesTheDocumentAndEveryVariable()
    {
        var request = LinQLDeclaredQueryRequest.Bind("query linql { __typename }", ("var1", "1"), ("var2", null));

        request.Document.Should().Be("query linql { __typename }");
        request.Variables.Should().HaveCount(2);
        request.Variables["var1"].Should().Be("1");
        request.Variables["var2"].Should().BeNull();
    }

    [Fact]
    public void BindNormalisesValuesTheWayRuntimeArgumentsAre()
    {
        var request = LinQLDeclaredQueryRequest.Bind("query linql { __typename }", ("var1", new { Name = "widget" }));

        var value = request.Variables["var1"].Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;

        value.Should().ContainKey("name").WhoseValue.Should().Be("widget");
    }
}
