#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

public partial class TranslationProviderTests
{
    [Fact]
    public void SelectAllNestedFields() => this.Test<NestedClassType, object>(x => x.Nested!);

    [Fact]
    public void SelectAllWithHelperNestedFields() => this.Test<NestedClassType, object>(x => x.Nested!.SelectAll());

    [Fact]
    public void SelectAllNestedFieldsFromOperation() => this.Test<NestedOperationType, object>(x => x.Operation.GetNumber("123")!);

    [Fact]
    public void SelectAllNestedFieldsWithHelperFromOperation() => this.Test<NestedOperationType, object>(x => x.Operation.GetNumber("123")!.SelectAll());

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

    [Fact]
    public void ProjectArrayOfInterface()
        => this.Test<InterfaceRootType, object>(x => x.ArrayOfInterfaces.Select(a => new Projection(a.Number, a.Text)));

    [Fact]
    public void ProjectIEnumerableOfInterface()
        => this.Test<InterfaceRootType, object>(x => x.EnumerableOfInterfaces.Select(a => new Projection(a.Number, a.Text)));

    [Fact]
    public void ProjectListOfInterface()
        => this.Test<InterfaceRootType, object>(x => x.ListOfInterfaces.Select(a => new Projection(a.Number, a.Text)));

    [Fact]
    public void Project()
        => this.Test<NestedOperationType, Projection>(x => x.Operation.GetNumber("project me").Project(y => new Projection(y.Number, y.Text)));

    [Fact]
    public void ProjectAcrossMultipleLevels()
        => this.Test<NestedOperationType, Projection>(x => new Projection(x.Number, x.Operation.GetNumber("project me").Project(y => y.Text)));

    [Fact]
    public void ProjectSameOperationMultipleTimes()
        => this.Test<NestedOperationType, NestedProjection>(x => new NestedProjection(x.Operation.Float, x.Operation.GetNumber("project me").Project(y => new Projection(y.Number, y.Text))));
}
