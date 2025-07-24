All queries start from a `RootType<T>` which the source generator will create for
you.  You can then project a result from it using a C# `Expression` which has it's
limitations, much like with Entity Framework, but still very powerful.  This library
is fairly new, and if you've ever looked into Expression Trees, hopefully you'll 
understand that it will take time to cover all the bases.  A range of expressions 
can be converted as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs).  If you come
accross something that doesn't work, please raise an issue or a pull request with 
a supporting test.

### Field selection
By default, if an expression terminates on a type, it will get all the scalar fields
available on that type.  For example if you have a type like such:
```csharp
[OperationType]
public class Root
{
    public ExampleType ExampleType { get; set; }
}

public class ExampleType
{
    public int Number { get; set; }

    public string Text { get; set; }

    [GraphQLOperation]
    public bool IsTrue() => true;

    public IEnumerable<string> Strings { get; set; }

    public ExampleType Nested { get; set; }

    [GraphQLOperation]
    public ExampleType GetNestedById(int id) { get; set; }
}
```
If you were to query:
```csharp
(Root x) => x.ExampleType;
```
would produce:
```graphql
query {
    exampleType {
        number
        text
    }
}
```
And if you query:
If you were to query:
```csharp
(Root x) => x.ExampleType.Nested;
```
would produce:
```graphql
query {
    exampleType {
        nested {
            number
            text
        }
    }
}
```
The lambda synax we're using here might look a little odd, we're used to having the compiler
infer the parameter type IE `x => x` but here we're doing `(Root x) => x`.  This is mearly a
suggestion as it gives a hint to the compiler what generic types are in play instead of having
to do `Query<Root, int>(x => x.Number)` which is ever so slightly verbose.  Any suggestions on
how to improve this are welcome!

If you would like to explicitly request all fields there is a helper method `SelectAll`
so you could do
```csharp
(Root x) => x.ExampleType.SelectAll().Nested;
```
which would produce:
```graphql
query {
    exampleType {
        number
        text
        nested {
            number
            text
        }
    }
}
```

You can also of course project to get specific results:
```csharp
(Root x) => new { x.ExampleType.Number, x.ExampleType.Nested.Text, IsAvailable = x.ExampleType.IsTrue() };
```
Or if you suppose an array of ExampleTypes you can use the native LINQ `Select` on arrays too:
```csharp
(Root x) => x.ExampleTypes.Select(y => new { y.Number, y.Text, IsAvailable = y.IsTrue() });
```
You can then of course do whatever you like with the results
```csharp
(await Query((Root x) => x.ExampleTypes.Select(y => new { y.Number, y.Text, IsAvailable = y.IsTrue() })))
    .Where(x => x.IsAvailable).ToList();
```

Another scenario is where you have the results of an operation or a nested type that you need to retrieve fields from. In this instance you can use the `Project` helper method.
```csharp
(await Query((Root x) => new { Number = x.ExampleType.Number, Obj = x.ExampleType.GetByNumber(123).Project(y => new { y.Number, y.Text }) });
```