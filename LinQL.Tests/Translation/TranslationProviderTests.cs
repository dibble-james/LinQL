#pragma warning disable CA1852 // Seal internal types
#nullable enable
namespace LinQL.Tests.Translation;

using System;
using System.Linq.Expressions;
using LinQL.Description;
using LinQL.Expressions;
using LinQL.Translation;

public partial class TranslationProviderTests
{
    private void Test<TRoot, TData>(Expression<Func<TRoot, TData>> expression)
        where TRoot : RootType<TRoot>
        => this.TestInclude(expression, _ => { });

    private void TestInclude<TRoot, TData>(Expression<Func<TRoot, TData>> expression, Action<GraphQLExpression<TRoot, TData>> includes)
        where TRoot : RootType<TRoot>
    {
        var request = TranslationProvider.ToRequest(expression, new(), includes);

        Snapshot.Match(request.Query);
    }

    private record Projection(int Number, string? Text);

    private record NestedProjection(float Float, Projection Projection);

    [OperationType]
    private class SimpleScalarType : ISimpleType, RootType<SimpleScalarType>
    {
        public int Number { get; set; }

        public string? Text { get; set; }
    }

    [OperationType]
    private class NestedClassType : RootType<NestedClassType>
    {
        public SimpleScalarType? Nested { get; set; }

        public float Float { get; set; }
    }

    [OperationType]
    private class NestedArrayType : RootType<NestedArrayType>
    {
        public IEnumerable<SimpleScalarType> Types { get; set; } = null!;

        public float Float { get; set; }
    }

    [OperationType]
    private class SimpleOperationType : RootType<SimpleOperationType>
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber() => default!;

        public float Float { get; set; }
    }

    [OperationType]
    private class OperationWithScalarParametersType : RootType<OperationWithScalarParametersType>
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber([GraphQLArgument(GQLType = "String!")] string value) => new() { Text = value };

        public float Float { get; set; }
    }

    [OperationType]
    private class OperationWithTypeParametersType : RootType<OperationWithTypeParametersType>
    {
        [GraphQLOperation]
        public SimpleScalarType GetNumber([GraphQLArgument(GQLType = "SimpleScalarType!")] SimpleScalarType input) => input;

        public float Float { get; set; }
    }

    [OperationType]
    private class OperationWithNullableTypeParametersType : RootType<OperationWithNullableTypeParametersType>
    {
        [GraphQLOperation]
        public SimpleScalarType? GetNumber([GraphQLArgument(GQLType = "SimpleScalarType")] SimpleScalarType? input) => input;

        public float Float { get; set; }
    }

    [OperationType]
    private class NestedOperationType : RootType<NestedOperationType>
    {
        public OperationWithScalarParametersType Operation => default!;

        public string Text => default!;

        public int Number { get; set; }
    }

    [OperationType]
    private class NestedOperationInOperationType : RootType<NestedOperationInOperationType>
    {
        [GraphQLOperation]
        public NestedOperationType Operation([GraphQLArgument(GQLType = "Int!")] int number) => new() { Number = number };

        public string Text => default!;
    }

    [OperationType]
    private class RenamedType : RootType<RenamedType>
    {
        [GraphQLField(Name = "notText")]
        public string Text => default!;


        [GraphQLField(Name = "object")]
        [GraphQLOperation()]
        public SimpleScalarType GetObject() => default!;
    }

    private interface ISimpleType
    {
        public string? Text { get; }

        public int Number { get; }
    }

    private class SomeOtherSimpleType : ISimpleType
    {
        public string? Text { get; }

        public int Number { get; }

        public float Float { get; }

        public OperationWithScalarParametersType Operation { get; } = default!;
    }

    [OperationType]
    private class InterfaceRootType : RootType<InterfaceRootType>
    {
        public required ISimpleType SimpleType { get; set; }

        public ISimpleType[] ArrayOfInterfaces { get; set; } = [];

        public IEnumerable<ISimpleType> EnumerableOfInterfaces { get; set; } = [];

        public List<ISimpleType> ListOfInterfaces { get; set; } = [];
    }

    [OperationType(RootOperationType.Query)]
    public class ScalarOnRootType : RootType<ScalarOnRootType>
    {
        public int Number { get; set; }

        [GraphQLOperation, GraphQLField(Name = "number")]
        public int GetNumber() => this.Number;
    }

    [OperationType(RootOperationType.Query)]
    public class ScalarArray : RootType<ScalarArray>
    {
        public required string[] Strings { get; set; }

        [GraphQLOperation, GraphQLField(Name = "numbers")]
        public int[] GetNumbers() => [];

        [GraphQLOperation, GraphQLField(Name = "filteredNumbers")]
        public int[] GetFilteredNumbers([GraphQLArgument(GQLType = "Int!")] int number) => [number];
    }
}
