namespace LinQL.Tests.Translation;

using System.Linq.Expressions;
using LinQL.Description;
using LinQL.Translation;

public class TranslationProviderTests
{
    private readonly TranslationProvider target;
    private readonly Graph fakeGraph;

    public TranslationProviderTests()
        => (this.target, this.fakeGraph) = (new TranslationProvider(), Substitute.For<Graph>(Substitute.For<IGraphQLConnection>(), this.target));

    [Fact]
    public void Simple()
        => this.Test<SimpleScalarType, object>(x => new { x.Number, x.Text });

    [Fact]
    public void NestedMemberFlatten()
        => this.Test<NestedClassType, object>(x => new { x.Float, x.Nested!.Number, x.Nested.Text });

    [Fact]
    public void NestedMember()
        => this.Test<NestedClassType, object>(x => new { x.Float, Nested = new { x.Nested!.Number } });

    [Fact]
    public void SimpleOperation()
        => this.Test<SimpleOperationType, object>(x => new { x.Float, x.GetNumber().Number });

    [Fact]
    public void OperationWithConstantParameters()
        => this.Test<OperationWithScalarParametersType, object>(x => new { x.Float, x.GetNumber("123").Number });

    [Fact]
    public void OperationWithObjectParameters()
        => this.Test<OperationWithTypeParametersType, object>(x => new { x.Float, x.GetNumber(new SimpleScalarType()).Number });

    [Fact]
    public void OperationWithVariableMemberParameters()
    {
        var input = new SimpleScalarType { Number = 123, Text = "321" };

        this.Test<OperationWithScalarParametersType, object>(x => new { x.GetNumber(input.Text).Number });
    }

    [Fact]
    public void OperationWithVariableParameters()
    {
        var input = new SimpleScalarType { Number = 123, Text = "321" };

        this.Test<OperationWithTypeParametersType, object>(x => new { x.GetNumber(input).Number });
    }

    [Fact]
    public void NestedOperationWithVariableParameters()
    {
        var input = new SimpleScalarType { Number = 123, Text = "321" };

        this.Test<NestedOperationType, object>(x => new
        {
            x.Operation.GetNumber(input.Text).Number,
            x.Text,
        });
    }

    [Fact]
    public void NestedOperationInOperation()
    {
        var input = new SimpleScalarType { Number = 123, Text = "321" };

        this.Test<NestedOperationInOperationType, object>(x => new
        {
            x.Operation(input.Number).Operation.GetNumber(input.Text).Number,
            x.Text,
        });
    }

    [Fact]
    public void SelectAllFields() => this.Test<SimpleScalarType, object>(x => x);

    [Fact]
    public void SelectAllNestedFields() => this.Test<NestedClassType, object>(x => x.Nested!);

    [Fact]
    public void SelectAllNestedFieldsFromOperation() => this.Test<NestedOperationType, object>(x => x.Operation.GetNumber("123")!);

    [Fact]
    public void SelectFromArray() => this.Test<NestedArrayType, object>(x => x.Types.Select(x => new
    {
        x.Text,
        x.Number,
    }));

    [Fact]
    public void RenameField() => this.Test<RenamedType, object>(x => x.Text);

    [Fact]
    public void RenameOperation() => this.Test<RenamedType, object>(x => x.GetObject());

    private void Test<TRoot, TData>(Expression<Func<TRoot, TData>> expression)
    {
        var graphExpression = this.target.ToExpression(this.fakeGraph, expression);
        Snapshot.Match(this.target.ToQueryString(graphExpression));
    }

    [OperationType]
    private class SimpleScalarType
    {
        public int Number { get; set; }

        public string? Text { get; set; }
    }

    [OperationType]
    private class NestedClassType
    {
        public SimpleScalarType? Nested { get; set; }

        public float Float { get; set; }
    }

    [OperationType]
    private class NestedArrayType
    {
        public IEnumerable<SimpleScalarType> Types { get; set; } = null!;

        public float Float { get; set; }
    }

    [OperationType]
    private class SimpleOperationType
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber() => default!;

        public float Float { get; set; }
    }

    [OperationType]
    private class OperationWithScalarParametersType
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber(string value) => new() { Text = value };

        public float Float { get; set; }
    }

    [OperationType]
    private class OperationWithTypeParametersType
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber(SimpleScalarType input) => input;

        public float Float { get; set; }
    }

    [OperationType]
    private class NestedOperationType
    {
        public OperationWithScalarParametersType Operation => default!;

        public string Text => default!;

        public int Number { get; set; }
    }

    [OperationType]
    private class NestedOperationInOperationType
    {
        [GraphQLOperation]
        public NestedOperationType Operation(int number) => new() { Number = number };

        public string Text => default!;
    }

    [OperationType]
    private class RenamedType
    {
        [GraphQLField(Name = "notText")]
        public string Text => default!;


        [GraphQLField(Name = "object")]
        [GraphQLOperation()]
        public SimpleScalarType GetObject() => default!;
    }
}
