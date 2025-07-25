#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

public partial class TranslationProviderTests
{
    [Fact]
    public void ScalarOnRoot()
        => this.Test<ScalarOnRootType, object>(x => x.GetNumber());

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
    public void BasicInclude()
        => this.TestInclude<NestedOperationType, object>(x => x.Operation, x => x.Include(y => y.Operation.GetNumber("123")));

    [Fact]
    public void ScalarArrayFields()
        => this.Test<ScalarArray, string[]>(x => x.Strings);
}
