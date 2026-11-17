namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using HotChocolate.Language;
using LinQL.Description;

// The schema an SDL document describes, as the walk that reads the document found it.
//
// The walk that fills this in and the emitter that renders it are separate passes. The model is read-only once the
// walk is under way: the collections are only changed through the Add methods, which the walker alone calls, and
// everything else reads the read-only views. Naming lives in SchemaNames, because those rules need no schema.
internal sealed class SchemaModel
{
    private readonly List<RootTypeClass> rootOperations = [];
    private readonly List<IClassFactory> types = [];
    private readonly List<FieldExtension> fieldExtensions = [];
    private readonly List<DeclaredType> declaredTypes = [];
    private readonly Dictionary<string, DeclaredType> declaredTypesByName = new(StringComparer.Ordinal);
    private readonly List<EntityKeyPattern> entityKeyPatterns = [];
    private readonly Dictionary<string, EntityKey> entityKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> enumTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scalar> scalars = Scalar.NativeScalars.ToDictionary(x => x.Name, StringComparer.InvariantCultureIgnoreCase);

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
    /// Gets every type that declares fields, in document order.
    /// </summary>
    /// <remarks>
    /// Object, interface and input object declarations are all recorded, so the emitters read a type's
    /// fields from the model instead of keeping their own copy. The filter diagnostics iterate the
    /// selectable ones in this order, because the first field returning a type decides which argument
    /// name the generated extension uses.
    /// </remarks>
    public IReadOnlyList<DeclaredType> DeclaredTypes => this.declaredTypes;

    /// <summary>
    /// Gets every declared type that has fields by name.
    /// </summary>
    public IReadOnlyDictionary<string, DeclaredType> DeclaredTypesByName => this.declaredTypesByName;

    /// <summary>
    /// Gets every scalar a document can use: the built-in ones first, then the ones the document
    /// extends the schema with, in document order.
    /// </summary>
    public IReadOnlyDictionary<string, Scalar> KnownScalars => this.scalars;

    /// <summary>
    /// Gets the <c>@key</c> patterns the document's extensions declare, in document order.
    /// </summary>
    /// <remarks>
    /// A pattern with no type name is the global one, which applies to every object type that declares the
    /// field. The walker resolves the patterns once the whole document has been read, because a pattern may
    /// name a type or field that is declared later in it.
    /// </remarks>
    public IReadOnlyList<EntityKeyPattern> EntityKeyPatterns => this.entityKeyPatterns;

    /// <summary>
    /// Gets the field each entity type is identified by, by type name.
    /// </summary>
    public IReadOnlyDictionary<string, EntityKey> EntityKeys => this.entityKeys;

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
    /// Records a type and the fields it declares.
    /// </summary>
    /// <param name="name">The type's name.</param>
    /// <param name="kind">What the document declares the type as.</param>
    /// <param name="fields">The fields it declares, in document order.</param>
    public void AddDeclaredType(string name, DeclaredTypeKind kind, IReadOnlyList<FieldDefinitionNode> fields)
    {
        var type = new DeclaredType(name, kind, fields);
        this.declaredTypes.Add(type);
        this.declaredTypesByName[name] = type;
    }

    /// <summary>
    /// Records a <c>@key</c> pattern the document declares.
    /// </summary>
    /// <param name="pattern">The pattern.</param>
    public void AddEntityKeyPattern(EntityKeyPattern pattern) => this.entityKeyPatterns.Add(pattern);

    /// <summary>
    /// Records the key an entity type is identified by.
    /// </summary>
    /// <param name="key">The key.</param>
    public void AddEntityKey(EntityKey key) => this.entityKeys[key.TypeName] = key;

    /// <summary>
    /// Records an enum the document declares.
    /// </summary>
    /// <param name="name">The enum's name.</param>
    /// <remarks>
    /// Enums are leaves for the purpose of an entity key, so they are recorded apart from the types that
    /// declare fields: a key may name one, and the emitters never read their members.
    /// </remarks>
    public void AddEnumType(string name) => this.enumTypes.Add(name);

    /// <summary>
    /// Gets whether a GraphQL type is a leaf.
    /// </summary>
    /// <param name="typeName">The type's name.</param>
    /// <returns><see langword="true"/> for a scalar or an enum.</returns>
    public bool IsLeafType(string typeName)
        => this.scalars.ContainsKey(typeName) || this.enumTypes.Contains(typeName);

    /// <summary>
    /// Gets the fields a type declares.
    /// </summary>
    /// <param name="typeName">The type's name.</param>
    /// <returns>
    /// The fields in document order, or an empty list for a type that declares none, such as a union.
    /// </returns>
    public IReadOnlyList<FieldDefinitionNode> FieldsOf(string typeName)
        => this.DeclaredTypesByName.TryGetValue(typeName, out var type) ? type.Fields : [];

    /// <summary>
    /// Records a scalar the document declared.
    /// </summary>
    /// <param name="scalar">The scalar.</param>
    public void AddScalar(Scalar scalar) => this.scalars.Add(scalar.Name, scalar);

    /// <summary>
    /// Gets whether the document declares the input object.
    /// </summary>
    /// <param name="name">The type name.</param>
    /// <returns><see langword="true"/> when an input object with that name was declared.</returns>
    public bool HasInputObjectType(string name)
        => this.DeclaredTypesByName.TryGetValue(name, out var type) && type.Kind == DeclaredTypeKind.InputObject;

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

// A type a document declares and the fields it declares.
internal sealed record DeclaredType(string Name, DeclaredTypeKind Kind, IReadOnlyList<FieldDefinitionNode> Fields)
{
    /// <summary>
    /// Gets a value indicating whether a query can select fields from the type.
    /// </summary>
    /// <remarks>
    /// Only these types take part in the filter diagnostics: an input object's fields are argument
    /// declarations, not query fields, even though the emitters read both from the model.
    /// </remarks>
    public bool IsSelectable => this.Kind is DeclaredTypeKind.Object or DeclaredTypeKind.Interface;
}

internal sealed record EntityKeyPattern(string? TypeName, IReadOnlyList<string> Fields, Location? Location);

internal sealed record EntityKey(string TypeName, string FieldName);

internal enum DeclaredTypeKind
{
    /// <summary>An object type, including a root operation type.</summary>
    Object,

    /// <summary>An interface type.</summary>
    Interface,

    /// <summary>An input object type.</summary>
    InputObject,
}
