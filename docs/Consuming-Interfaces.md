Interfaces types must be registered in the Graph so that they can be deserialised correctly, not require large reflection calls and improve security (some known exploits exist with unconstrained deserialization).

The source generator will generate a helper extension method
```csharp
new JsonSerializerOptions().WithKnownInterfaces();
```
or you can register types manually

```csharp
new JsonSerializerOptions().RegisterInterface<IAmInterface>(typeof(ConcreateA), typeof(ConcreteB));
```

You can then use the concrete types in your queries in a number of ways:
```csharp
graph.Queries.Select(q => q.GetVehichle() as Car);
```
```csharp
graph.Queries.Select(q => (Car)q.GetVehichle());
```
```csharp
graph.Queries.Select(q => q.GetVehichles().OfType<Car>());
```
```csharp
graph.Queries.Select(q => q.GetVehichle()
    .On((Car c) => c.Model)
    .On((Van v) => v.Manufacturer));
```