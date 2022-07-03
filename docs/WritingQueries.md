# Writing Queries

All queries start from a `RootType`, which you then `Select` from.  You can then
project a result from it using a C# `Expression` which has it's limitations, much
like with Entity Framework, but still very powerful.  This library is fairly new,
and if you've ever looked into Expression Trees, hopefully you'll understand that
it will take time to cover all the bases.  A range of expressions can be converted 
as demonstrated by [the `TranslationProviderTests`](https://github.com/dibble-james/LinQL/blob/interface-support/LinQL.Tests/Translation/TranslationProviderTests.cs).  If you come accross something that 
doesn't work, please raise an issue or a pull request with a supporting test.

`Select` will produce a `GraphQLExpression` which you can then call `Execute` upon
to send it to the server and get back the result.

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
}
```
If you were to query:
```csharp
graph.Query.Select(x => x.ExampleType);
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
graph.Query.Select(x => x.ExampleType.Nested);
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

If you would like to explicitly request all fields there is a helper method `SelectAll`
so you could do
```csharp
graph.Query.Select(x => x.ExampleType.SelectAll().Nested);
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

Can also of course project to get specific results:
```csharp
graph.Query.Select(x => new { x.ExampleType.Number, x.ExampleType.Nested.Text, IsAvailable = x.ExampleType.IsTrue() });
```
Or if you suppose an array of ExampleTypes you can use the native LINQ `Select` on arrays too:
```csharp
graph.Query.Select(x => x.ExampleTypes.Select(y => new { y.Number, y.Text, IsAvailable = y.IsTrue() });
```
You can then of course do whatever you like with the results
```csharp
(await graph.Query.Select(x => x.ExampleTypes.Select(y => new { y.Number, y.Text, IsAvailable = y.IsTrue() })
    .Execute())
    .Where(x => x.IsAvailable).ToList();
```