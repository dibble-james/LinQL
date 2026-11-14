namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using LinQL.Description;

/// <summary>
/// The schema an SDL document describes, as the walk that reads the document found it.
/// </summary>
/// <remarks>
/// The walk that fills this in and the emitter that renders it are separate passes. The model is
/// read-only once the walk is under way: the collections are only changed through the <c>Add</c>
/// methods, which the walker alone calls, and everything else reads the read-only views.
/// Naming lives in <see cref="SchemaNames"/>, because those rules need no schema.
/// </remarks>
internal sealed class SchemaModel
{
    private readonly List<RootTypeClass> rootOperations = [];
    private readonly List<IClassFactory> types = [];
    private readonly List<FieldExtension> fieldExtensions = [];
    private readonly List<DeclaredType> declaredTypes = [];
    private readonly Dictionary<string, DeclaredType> declaredTypesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scalar> scalars = Scalar.NativeScalars.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);
    private readonly HashSet<string> inputObjectTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the root operations the document declares.
    /// </summary>
    public IReadOnlyList<RootTypeClass> RootOperations => this.rootOperations;

    /// <summary>
    /// Gets every type the document declares, in document order.
    /// </summary>
    public IReadOnlyList<IClassFactory> Types => this.types;

    /// <summary>
    /// Gets the field arguments that get an extension method.
    /// </summary>
    public IReadOnlyList<FieldExtension> FieldExtensions => this.fieldExtensions;

    /// <summary>
    /// Gets the object and interface types the document declares, in document order.
    /// </summary>
    /// <remarks>
    /// The filter diagnostics iterate this order because the first field returning a type decides
    /// which argument name the generated extension uses.
    /// </remarks>
    public IReadOnlyList<DeclaredType> DeclaredTypes => this.declaredTypes;

    /// <summary>
    /// Gets the declared object and interface types by name.
    /// </summary>
    public IReadOnlyDictionary<string, DeclaredType> DeclaredTypesByName => this.declaredTypesByName;

    /// <summary>
    /// Gets every scalar a document can use: the built-in ones first, then the ones the document
    /// extends the schema with, in document order.
    /// </summary>
    public IReadOnlyDictionary<string, Scalar> KnownScalars => this.scalars;

    /// <summary>
    /// Records a root operation.
    /// </summary>
    /// <param name="operation">The root type declaration.</param>
    public void AddRootOperation(RootTypeClass operation) => this.rootOperations.Add(operation);

    /// <summary>
    /// Records a type the document declares.
    /// </summary>
    /// <param name="type">The type.</param>
    public void AddType(IClassFactory type) => this.types.Add(type);

    /// <summary>
    /// Records a field argument that gets an extension method.
    /// </summary>
    /// <param name="extension">The extension.</param>
    public void AddFieldExtension(FieldExtension extension) => this.fieldExtensions.Add(extension);

    /// <summary>
    /// Records an object or interface type and the fields it declares.
    /// </summary>
    /// <param name="name">The type's name.</param>
    /// <param name="fields">The fields it declares, in document order.</param>
    public void AddDeclaredType(string name, IReadOnlyList<FieldDefinitionNode> fields)
    {
        var type = new DeclaredType(name, fields);
        this.declaredTypes.Add(type);
        this.declaredTypesByName[name] = type;
    }

    /// <summary>
    /// Records a scalar the document declared.
    /// </summary>
    /// <param name="scalar">The scalar.</param>
    public void AddScalar(Scalar scalar) => this.scalars.Add(scalar.Name, scalar);

    /// <summary>
    /// Records an input object the document declared.
    /// </summary>
    /// <param name="name">The type name.</param>
    public void AddInputObjectType(string name) => this.inputObjectTypes.Add(name);

    /// <summary>
    /// Gets whether the document declares the input object.
    /// </summary>
    /// <param name="name">The type name.</param>
    /// <returns><see langword="true"/> when an input object with that name was declared.</returns>
    public bool HasInputObjectType(string name) => this.inputObjectTypes.Contains(name);

    /// <summary>
    /// Gets the C# type a field or argument of this GraphQL type is declared as.
    /// </summary>
    /// <param name="type">The GraphQL type.</param>
    /// <returns>The C# type name, with list and nullability suffixes applied.</returns>
    public string TypeName(ITypeNode type)
    {
        var typeName = this.scalars.TryGetValue(type.NamedType().Name.Value, out var mapped)
            ? mapped.RuntimeType : type.NamedType().Name.Value;

        typeName = type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType()) ? typeName + "[]" : typeName;

        return type.IsNonNullType() ? typeName : typeName + "?";
    }
}

/// <summary>
/// An object or interface type a document declares.
/// </summary>
/// <param name="Name">The type's name.</param>
/// <param name="Fields">The fields it declares, in document order.</param>
internal sealed record DeclaredType(string Name, IReadOnlyList<FieldDefinitionNode> Fields);
