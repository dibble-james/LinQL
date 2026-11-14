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

### Adding extra fields with `Includes`

The document only asks for the fields the projection uses. When something other than the projection
needs a field — an id for a cache key, a related object you only inspect afterwards — add it with
`Includes`:

```csharp
var films = await Query((Root x) => x
    .Films
    .Includes(f => f.Characters.Select(c => c.Name))
    .Select(f => new { f.Title }));
```

`Includes` is a translation-only modifier: it returns its receiver and has no effect at runtime, so the
projection in `OriginalQuery` is unchanged. The fields its lambda selects are added to the field it is
called on, which means it can target a nested selection rather than only the document root:

```csharp
(Root x) => x.Film.Includes(f => f.Director)
```

adds `director` to the `film` selection. On a collection the lambda receives the element type, so
`x.Films.Includes(f => …)` reads `f` as a film. The order of `Includes`, `Filter`, `First`, `Sort` and
`Select` does not matter: each call attaches to the field its receiver resolves to.

The generated types implement `ICanHaveIncludes`, and `Includes` is constrained to it, so a scalar or an
input object is a compile error rather than a rejected query. A hand-written client type implements the
interface to opt in.

Because the extra field is part of the document, it is part of the operation cache key, so a query with
and without the include are two cached operations.

### Filtering

Hot Chocolate filtering is a resolver *argument* (`products(where: ProductFilterInput)`), not a
selection modifier, so LinQL exposes it as its own operation rather than translating `Where`.
The reasoning is covered in [Why not `Where`](#why-not-where).

#### The generated extensions

When an SDL field takes an argument whose input type is named `<Something>FilterInput`, the
generated client gets a `Filter` extension method for the field's type. For this SDL:

```graphql
type Query {
  products(where: ProductFilterInput): [Product!]!
}
```

the generator produces an extension that takes a builder for the filter, plus a builder class for
every filter input type:

```csharp
public static class FieldExtensions
{
    [GraphQLFilter(Argument = "where", GQLType = "ProductFilterInput")]
    public static Product[] Filter(
        this Product[] source,
        Expression<Func<ProductFilterInputBuilder, ProductFilterInputBuilder>> filter) => source;
}

public partial class ProductFilterInputBuilder
{
    [GraphQLFilterField(Name = "price")]
    public ProductFilterInputBuilder Price(Func<DecimalOperationFilterInputBuilder, DecimalOperationFilterInputBuilder> price) => this;

    [GraphQLFilterField(Name = "name")]
    public ProductFilterInputBuilder Name(Func<StringOperationFilterInputBuilder, StringOperationFilterInputBuilder> name) => this;
}
```

Calling it sends the filter as a variable:

```csharp
var products = await client.SendAsync((Root q) => q
    .Products
    .Filter(f => f
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
the input type needs the extension and builders added by hand for now.

Because the extension is declared on the field's type rather than on the field, one `Filter` method
serves every field returning that type, so those fields have to name the filter argument the same
way. Hot Chocolate's convention does that schema-wide; when an SDL disagrees the generator reports
`LINQLGEN06` on the argument that would be ignored.

The generated input objects still work too: pass one to `Filter` instead of a lambda and it is
normalised the same way as a builder, with unset operations dropped. That only works because the generated input
types declare every operation nullable. A hand-written filter type has to do the same — a
non-nullable value type (`decimal`, `bool`) cannot say "not set", so normalising one throws rather
than silently filtering on `0` or `false`.

#### Fields with other arguments

A filterable field usually takes more than the filter — `products(where:, order:, first:)` under
Hot Chocolate's paging conventions. Each of those arguments gets an extension method of its own,
named after the argument, so the call site reads like the field's argument list:

```graphql
type Query {
  featured(first: Int!, where: ProductFilterInput, order: [ProductSortInput!]): [Product!]!
}
```

```csharp
[GraphQLArgument(Name = "first", GQLType = "Int!")]
public static Product[] First(this Product[] source, int first) => source;

[GraphQLArgument(Name = "order", GQLType = "[ProductSortInput!]")]
public static Product[] Order(this Product[] source, ProductSortInput[]? order = null) => source;

[GraphQLArgument(Name = "order", GQLType = "[ProductSortInput!]")]
public static Product[] Sort(this Product[] source, ProductSortInput[]? order = null) => source;
```

`order` also gets a `Sort` alias, because ordering is the one argument every call site reads as a verb;
both attach the same `order` argument. The other arguments keep the schema's spelling, so the call
site still names what the field declares.

There is no fixed list of paging names: every argument a *filterable* field declares gets a method,
so `first`, `after`, `last`, `before` or a custom name all arrive on their own — the small schema
above simply declares only `first`. A field with no filter argument gets no extensions at all, so a
paged field that takes no `where` has to be queried through its `Execute…` method.

```csharp
client.SendAsync((Root q) => q
    .Featured
    .First(10)
    .Filter(f => f.Price(p => p.Gt(10)))
    .Sort(sorts)
    .Select(p => new { p.Name }));
```

```graphql
query linql($var1: Int!, $var2: ProductFilterInput, $var3: [ProductSortInput!]) {
  featured(first: $var1, where: $var2, order: $var3) {
    name
  }
}
```

The order of the calls does not matter: each one adds an argument to the field the expression
selects, so a filter can be applied to a paged field without a special signature for every
combination. A required GraphQL argument is a required parameter, so a document the server would
reject for a missing argument does not compile. Sorting and paging themselves are still not
translated — the value is attached as an argument, nothing more.

Argument values are sent in the shape GraphQL variables take rather than their CLR shape: input
object fields are camelCased (or use their `[JsonPropertyName]` when the type declares one),
properties marked `[JsonIgnore]` are left out, enums go out by name, unset fields are dropped, a
dictionary stays a JSON object with its keys unchanged, a type that declares `[JsonConverter]` is
written by that converter, and a `JsonElement` is written as the JSON it already holds, so
`new ProductSortInput { Price = SortEnumType.DESC }` becomes `{"price":"DESC"}`.

#### Only the operations you set are sent

The builder records only the operations you call, and a generated input object passed to `Filter`
is normalised the same way: unset properties, including empty nested objects, are
dropped, so only what you set appears in the variable. Enums are sent by name.

This matters because Hot Chocolate treats `null` as a **value**, not as absence: on a nullable
property `{ "eq": null }` means `IS NULL` and silently narrows the results, and on a non-nullable
property it is an error. An entirely empty filter (`{}`) is worse: Hot Chocolate 13 throws and
Hot Chocolate 16 silently returns every row. LinQL refuses to send either — a filter with no
operations throws an `InvalidOperationException` instead, so omit the extension to query without a
filter.

#### Nested objects, `and`/`or` and collections

Nested inputs work exactly like scalar ones:

```csharp
.Filter(f => f.Brand(b => b.Name(n => n.Eq("acme"))))
// => { "brand": { "name": { "eq": "acme" } } }
```

`And` and `Or` take further filter inputs. Anything already set on the filter is folded into the
first branch of the combinator, because sibling fields are `AND` in Hot Chocolate:

```csharp
.Filter(f => f.Price(p => p.Gt(10)).Or(o => o.Name(n => n.Eq("widget"))))
// => { "or": [ { "price": { "gt": 10 } }, { "name": { "eq": "widget" } } ] }
```

Collection filters use the wrapper Hot Chocolate generates for the element type. It has `some`,
`all`, `none` and `any` and nothing else:

```csharp
.Filter(f => f.Tags(t => t.Some(s => s.Label(l => l.Eq("new")))))
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

- Translating sorting (`order`) and paging. Any argument a filterable field declares can be attached
  — `first`, `after`, `last`, `before`, `order` — but the values are passed through as given, and the
  `…Connection` shape that cursor paging middleware returns has no selection support yet.
- Filtering an interface-typed field by its implementations' fields. Hot Chocolate generates the
  filter input from the interface itself (`SearchResultFilterInput`, holding only the interface's
  fields), and the generator follows the SDL. A filter *is* accepted on an interface-returning
  resolver — the end-to-end tests cover exactly that — but only that input type can be sent.

#### Prerequisites

The SDL must contain the filter input types, so export it from a server with `AddFiltering()` or
`[UseFiltering]`. Hot Chocolate 16 disables introspection by default; use `GET /graphql?sdl` or
the Hot Chocolate CLI to produce the SDL.

Filtering lives in `LinQL` itself, so the Strawberry Shake adapter supports it the same way.