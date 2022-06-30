# Consuming Interfaces

Interfaces types must be regisered in the Graph so that they can be deserialised correctly, not require large reflection calls and improve security (some known exploits exist with unconstrained deserialization).

```csharp
services.AddGraphQLClient<StarWarsGraph>()
    .AddInterfaceType<IAmInterface>(typeof(ConcreateA), typeof(ConcreteB))
    ...;
```
