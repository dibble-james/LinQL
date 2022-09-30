# Consuming Interfaces

Interfaces types must be regisered in the Graph so that they can be deserialised correctly, not require large reflection calls and improve security (some known exploits exist with unconstrained deserialization).

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