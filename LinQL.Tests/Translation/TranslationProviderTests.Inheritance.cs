#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

using LinQL.Description;

public partial class TranslationProviderTests
{
    [Fact]
    public void SimpleSelectInterface() => this.Test<InterfaceRootType, object>(x => x.SimpleType!.Number);

    [Fact]
    public void SelectInterfaceCastConcreteType()
        => this.Test<InterfaceRootType, object>(x => ((SomeOtherSimpleType)x.SimpleType!)!.Float);

    [Fact]
    public void SelectAllFieldsInterfaceCastConcreteType()
        => this.Test<InterfaceRootType, object>(x => (SomeOtherSimpleType)x.SimpleType!);

    [Fact]
    public void SelectInterfaceAsConcreteType()
        => this.Test<InterfaceRootType, object>(x => (x.SimpleType as SomeOtherSimpleType)!.Float);

    [Fact]
    public void SelectAllFieldsInterfaceAsConcreteType()
        => this.Test<InterfaceRootType, object>(x => (x.SimpleType as SomeOtherSimpleType)!);

    [Fact]
    public void SelectAllFieldsInterfaceOnConcreteType()
        => this.Test<InterfaceRootType, object?>(x =>
            x.SimpleType.On((SomeOtherSimpleType y) => y.Number.ToString())
                        .On((SimpleScalarType y) => y.Text!));

    [Fact]
    public void SelectInterfaceArray()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.OfType<SomeOtherSimpleType>().Select(y => new { y.Number, y.Text, y.Float }));

    [Fact]
    public void SelectInterfaceArrayAllFieldsOnType()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.OfType<SomeOtherSimpleType>());

    [Fact]
    public void SelectInterfaceArrayCastConcreteType()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.Select(y => new { ((SomeOtherSimpleType)y).Float, y.Text }));

    [Fact]
    public void SelectInterfaceArrayAsConcreteType()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.Select(y => new { (y as SomeOtherSimpleType)!.Float, y.Text }));

    [Fact]
    public void SelectInterfaceArrayAsConcreteTypeWithOperation()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.Select(y => new { Number = (y as SomeOtherSimpleType)!.Operation.GetNumber("123"), y.Text }));

    [Fact]
    public void SelectInterfaceArrayOnConcreteType()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces
            .Select(y => y.On((SimpleScalarType z) => z.Text)
                          .On((SomeOtherSimpleType z) => z.Float.ToString()!)));

    [Fact]
    public void SelectInterfaceArrayMultipleTypes()
        => this.Test<InterfaceRootType, object>(x => new
        {
            Types = x.ArrayOfInterfaces.OfType<SimpleScalarType>().Select(y => new { y.Number, y.Text }),
            OtherTypes = x.ArrayOfInterfaces.OfType<SomeOtherSimpleType>().Select(y => new { y.Number, y.Text, y.Float }),
        });

    [Fact]
    public void SelectInterfaceArrayMultipleTypesSelectAll()
        => this.Test<InterfaceRootType, object>(x => new
        {
            Types = x.ArrayOfInterfaces.OfType<SimpleScalarType>().Select(y => y.SelectAll()),
            OtherTypes = x.ArrayOfInterfaces.OfType<SomeOtherSimpleType>().Select(y => y.SelectAll()),
        });

    [Fact]
    public void MultipleProjectionsWithOn()
        => this.Test<MultipleTypeInheritence, object>(
            x => x.IAmObject
                .On((Implementaion1 y) => y.Text)
                .On((Implementaion2 y) => y.Value)
                .On((Implementaion3 y) => y.Date)
                .On((Implementaion4 y) => y.Amount)
                .On((Implementaion5 y) => new { y.Id, Texts = y.Collection.Select(z => z.Text) })
        );

    [OperationType]
    private class MultipleTypeInheritence : RootType<MultipleTypeInheritence>
    {
        public IAmInterface IAmObject { get; set; } = default!;
    }

    private interface IAmInterface;

    private record Implementaion1(string Text) : IAmInterface;

    private record Implementaion2(int Value) : IAmInterface;

    private record Implementaion3(DateOnly Date) : IAmInterface;

    private record Implementaion4(float Amount) : IAmInterface;

    private record Implementaion5(Guid Id, List<SimpleScalarType> Collection) : IAmInterface;
}