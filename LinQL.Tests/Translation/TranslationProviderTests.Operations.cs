#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

public partial class TranslationProviderTests
{
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
    public void OperationWithNullableVariableParameters()
    {
        var input = new SimpleScalarType { Number = 123, Text = "321" };

        this.Test<OperationWithNullableTypeParametersType, object>(x => new { x.GetNumber(input)!.Number });
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
    public void ScalarArrayOperation()
        => this.Test<ScalarArray, int[]>(x => x.GetNumbers());

    [Fact]
    public void ScalarArrayOperationWithArguments()
        => this.Test<ScalarArray, int[]>(x => x.GetFilteredNumbers(1));
}
