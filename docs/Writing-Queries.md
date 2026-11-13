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

### Filtering

Hot Chocolate filtering is a resolver *argument* (`products(where: ProductFilterInput)`), not a
selection modifier, so LinQL exposes it as its own operation rather than translating `Where`.
The reasoning is covered in [Why not `Where`](#why-not-where).

#### The generated companion

When an SDL field takes an argument whose input type is named `<Something>FilterInput`, the
generated root type gets a companion named `Filter{Field}`. For this SDL:

```graphql
type Query {
  products(where: ProductFilterInput): [Product!]!
}
```

the generator produces a companion that takes a builder for the filter, plus a builder class for
every filter input type:

```csharp
[GraphQLFilter(Field = "products", Argument = "where", GQLType = "ProductFilterInput")]
public Product[] FilterProducts(Expression<Func<ProductFilterInputBuilder, ProductFilterInputBuilder>> _) => Products;

public partial class ProductFilterInputBuilder
{
    [GraphQLFilterField(Name = "price")]
    public ProductFilterInputBuilder Price(Func<DecimalOperationFilterInputBuilder, DecimalOperationFilterInputBuilder> price) => this;

    [GraphQLFilterField(Name = "name")]
    public ProductFilterInputBuilder Name(Func<StringOperationFilterInputBuilder, StringOperationFilterInputBuilder> name) => this;
}
```

Calling the companion sends the filter as a variable:

```csharp
var products = await client.SendAsync((Root q) => q
    .FilterProducts(f => f
        .Price(p => p.Gt(10))
        .Name(n => n.Contains("widget")))
    .Select(p => new { p.Id, p.Name, p.Price }));
```

```graphql
query linql($var1: ProductFilterInput) {
  products(where: $var1) {
    id
    name
    price
  }
}
```

```json
{ "var1": { "price": { "gt": 10 }, "name": { "contains": "widget" } } }
```

The argument name and type come from the SDL, so a filter convention that renames the argument
still works, and every filter input type in the SDL gets a builder named `<TypeName>Builder`. The
`<Something>FilterInput` name is Hot Chocolate's convention and is what the generator looks for (a
type named exactly `FilterInput` is treated as an ordinary input); a custom convention that renames
the input type needs its companion and builders added by hand for now.

The generated input objects still work too: pass one to the companion and it is normalised the
same way as a builder, with unset operations dropped.

#### Only the operations you set are sent

The builder records only the operations you call, and a generated input object passed to the
companion is normalised the same way: unset properties, including empty nested objects, are
dropped, so only what you set appears in the variable. Enums are sent by name.

This matters because Hot Chocolate treats `null` as a **value**, not as absence: on a nullable
property `{ "eq": null }` means `IS NULL` and silently narrows the results, and on a non-nullable
property it is an error. An entirely empty filter (`{}`) is worse: Hot Chocolate 13 throws and
Hot Chocolate 16 silently returns every row. LinQL refuses to send either — a filter with no
operations throws an `InvalidOperationException` instead, so omit the argument (or the companion)
to query without a filter.

#### Nested objects, `and`/`or` and collections

Nested inputs work exactly like scalar ones:

```csharp
.FilterProducts(f => f.Brand(b => b.Name(n => n.Eq("acme"))))
// => { "brand": { "name": { "eq": "acme" } } }
```

`And` and `Or` take further filter inputs. Anything already set on the filter is folded into the
first branch of the combinator, because sibling fields are `AND` in Hot Chocolate:

```csharp
.FilterProducts(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget"))))
// => { "or": [ { "price": { "gt": 10 } }, { "name": { "eq": "widget" } } ] }
```

Collection filters use the wrapper Hot Chocolate generates for the element type. It has `some`,
`all`, `none` and `any` and nothing else:

```csharp
.FilterProducts(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new")))))
// => { "tags": { "some": { "label": { "eq": "new" } } } }
```

`some` is `Any`, `all` is `All` (vacuously true on an empty list), `none` is `!Any` and
`any: false` matches empty lists. `in`/`nin` belong to the element filter, so "has any of" is
`Some(s => s.Label(l => l.In(values)))`. There is no `not` field anywhere in the schema; use the
negated operations (`neq`, `nin`, `ncontains`, `ngt`, …).

#### Why not `Where`

`.Where(p => p.Price > 10)` is **not** translated into a filter argument. `ExpressionTranslator`
has no case for it, so the predicate stays in `OriginalQuery` and is applied by the adapter
*after* the response arrives: the full result set is still fetched and then filtered in memory.
There is no plan to map it onto `where`, because the two are not the same operation:

- Hot Chocolate's vocabulary is per schema (`gt`, `contains`, `some`, `in`, negated variants …),
  while a C# predicate is arbitrary. Any automatic mapping would silently change results —
  `p.Price > 10` and `price.gt(10)` already differ for nulls.
- `Where` cannot express `or` at the top level without rewriting the expression, and rewriting a
  predicate into the server's vocabulary is exactly the lossy translation this design avoids.
- It would move evaluation from the client to the server without the caller asking, which changes
  both the number of rows fetched and which rows are returned.

Filtering is therefore explicit and one-to-one with the schema. `Where` keeps working as before
for shaping results you already have.

#### Not covered

- Sorting (`order`) and paging (`first`, `after`, `last`, `before`). Both change the query shape or
  the result type and need their own design.
- Companions on interface-typed fields; only object and root types get one today.

#### Prerequisites

The SDL must contain the filter input types, so export it from a server with `AddFiltering()` or
`[UseFiltering]`. Hot Chocolate 16 disables introspection by default; use `GET /graphql?sdl` or
the Hot Chocolate CLI to produce the SDL.

Filtering lives in `LinQL` itself, so the Strawberry Shake adapter supports it the same way.