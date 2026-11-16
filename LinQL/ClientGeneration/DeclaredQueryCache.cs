namespace LinQL.ClientGeneration;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using HotChocolate.Language;
using Microsoft.CodeAnalysis;
using Location = Microsoft.CodeAnalysis.Location;

/// <summary>
/// Emits the entity-cache hook of a declared query.
/// </summary>
/// <remarks>
/// The hook is generated from the document the declaration compiles to, so it knows exactly which fields the
/// operation selected. That is what <c>Write</c> needs: the properties a selection did not cover are
/// copied forward from the entity the store already holds, and filled with <c>default!</c> on a first write, which
/// is the merge a generated Strawberry Shake client performs. <c>Rebuild</c> replaces the entities the
/// operation selected with the stored instances and returns the root to project again.
/// An operation is cached only when its whole selection can be described this way: no inline fragment, and an
/// entity whose key field is not selected cannot be identified, so it takes the operation out of entity caching
/// rather than half-populating the store. A declaration that opts out keeps the operation-level cache.
/// </remarks>
internal sealed class DeclaredQueryCache
{
    private readonly SchemaModel model;
    private readonly string clientNamespace;
    private readonly string declaration;
    private readonly Location location;
    private readonly List<Diagnostic> problems = [];
    private int locals;

    private DeclaredQueryCache(SchemaModel model, string clientNamespace, string declaration, Location location)
        => (this.model, this.clientNamespace, this.declaration, this.location, this.locals)
            = (model, clientNamespace, declaration, location, 0);

    /// <summary>
    /// Emits the cache class of a declared query.
    /// </summary>
    /// <param name="document">The document the declaration translated to.</param>
    /// <param name="model">The schema the declaration is bound to.</param>
    /// <param name="rootTypeName">The root operation type's name.</param>
    /// <param name="clientNamespace">The client namespace the schema declares.</param>
    /// <param name="name">The name of the class to emit.</param>
    /// <param name="declaration">The declaration, as it reads in a diagnostic.</param>
    /// <param name="location">The declaration's location.</param>
    /// <returns>
    /// The class, or <see langword="null"/> when the operation cannot be cached, and the reason it could not be if
    /// there is one to give.
    /// </returns>
    public static (string? Source, IReadOnlyList<Diagnostic> Problems) Emit(
        DocumentNode document,
        SchemaModel model,
        string rootTypeName,
        string clientNamespace,
        string name,
        string declaration,
        Location location)
    {
        var selection = document.Definitions
            .OfType<OperationDefinitionNode>()
            .FirstOrDefault()
            ?.SelectionSet;

        if (selection is null)
        {
            return (null, []);
        }

        var emitter = new DeclaredQueryCache(model, clientNamespace, declaration, location);
        var fields = emitter.Fields(selection, rootTypeName);

        if (fields is null)
        {
            return (null, emitter.problems);
        }

        // A schema without @key has no entities to normalise, and a cache hook that stores nothing would only
        // add an empty class to the generated client.
        return fields.Any(x => emitter.ContainsEntities(x.Definition.Type.NamedType().Name.Value, x.Children))
            ? (emitter.Class(fields, rootTypeName, name), emitter.problems)
            : (null, emitter.problems);
    }

    /// <summary>
    /// Reads one selection set as the fields the cache code walks.
    /// </summary>
    /// <param name="set">The selection set.</param>
    /// <param name="typeName">The type the set is selected from.</param>
    /// <returns>The fields, or <see langword="null"/> when the set cannot be cached.</returns>
    private List<CacheField>? Fields(SelectionSetNode set, string typeName)
    {
        var fields = new List<CacheField>();

        foreach (var selection in set.Selections)
        {
            if (selection is not FieldNode field)
            {
                // An inline fragment selects a concrete type through an interface or union, which the generated
                // type does not model as one CLR type, so the operation opts out rather than caching a guess.
                this.Report("it selects through an inline fragment");
                return null;
            }

            if (field.Name.Value == "__typename")
            {
                continue;
            }

            var definition = this.model.FieldsOf(typeName).FirstOrDefault(x => x.Name.Value == field.Name.Value);

            if (definition is null)
            {
                this.Report($"'{field.Name.Value}' is not a field of '{typeName}'");
                return null;
            }

            var childType = definition.Type.NamedType().Name.Value;
            List<CacheField> children = [];

            if (field.SelectionSet is { } childSet)
            {
                if (!this.model.DeclaredTypesByName.TryGetValue(childType, out var declared)
                    || declared.Kind != DeclaredTypeKind.Object)
                {
                    this.Report($"'{childType}' is an interface or union, and only an object type can be an entity");
                    return null;
                }

                if (this.Fields(childSet, childType) is not { } nested)
                {
                    return null;
                }

                children = nested;
            }
            else if (!this.model.IsLeafType(childType))
            {
                this.Report($"'{field.Name.Value}' returns '{childType}' without selecting its fields");
                return null;
            }

            // An entity is identified by its key, so an operation that does not select the key cannot say which
            // entity it read, and writing an arbitrary instance would invent one.
            if (children.Count > 0
                && this.model.EntityKeys.TryGetValue(childType, out var key)
                && !children.Any(x => x.Definition.Name.Value == key.FieldName))
            {
                this.problems.Add(Diagnostic.Create(
                    EntityCacheDiagnostics.MissingEntityKey,
                    this.location,
                    this.declaration,
                    childType,
                    key.FieldName));

                return null;
            }

            fields.Add(new CacheField(definition, children, IsList(definition.Type)));
        }

        return fields;
    }

    private static bool IsList(ITypeNode type)
        => type.IsListType() || (type.IsNonNullType() && type.InnerType().IsListType());

    /// <summary>
    /// Records why the operation cannot be normalised.
    /// </summary>
    /// <param name="reason">The reason, written to follow the declaration in the message.</param>
    private void Report(string reason)
    {
        // The first failure ends the walk, so a nested set cannot add a second reason for the same operation.
        if (this.problems.Count == 0)
        {
            this.problems.Add(Diagnostic.Create(
                EntityCacheDiagnostics.NotNormalisable,
                this.location,
                this.declaration,
                reason));
        }
    }

    /// <summary>
    /// Emits the cache class.
    /// </summary>
    /// <param name="fields">The root's selected fields.</param>
    /// <param name="rootTypeName">The root operation type's name.</param>
    /// <param name="name">The name of the class to emit.</param>
    /// <returns>The class.</returns>
    private string Class(IReadOnlyList<CacheField> fields, string rootTypeName, string name)
    {
        var root = $"global::{this.clientNamespace}.{rootTypeName}";
        var code = new StringBuilder();

        // The graph a response deserialises into can hold null where the schema promises a value, which is what
        // the emitted property assignments would otherwise warn about. The generated schema file suppresses the
        // equivalent warning for the same reason.
        code.AppendLine("#pragma warning disable CS8601");
        code.AppendLine();
        code.AppendLine($"/// <summary>");
        code.AppendLine($"/// Writes and rebuilds the entities the declared query selects.</summary>");
        code.AppendLine($"/// <remarks>Generated from the declaration's document; do not edit.</remarks>");
        code.AppendLine($"public sealed class {name}Cache : global::LinQL.Caching.LinQLOperationCache<{root}>");
        code.AppendLine("{");
        code.AppendLine($"    /// <summary>The shared instance.</summary>");
        code.AppendLine($"    public static readonly {name}Cache Instance = new();");
        code.AppendLine();
        code.AppendLine($"    private {name}Cache()");
        code.AppendLine("    {");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    /// <inheritdoc />");
        code.AppendLine($"    public override void Write({root} root, global::LinQL.Caching.IEntityWriter writer)");
        code.AppendLine("    {");

        foreach (var field in fields)
        {
            this.Value($"root.{SchemaNames.FieldName(field.Definition.Name.Value)}", field, rebuild: false, "writer", "w", code, 2);
        }

        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    /// <inheritdoc />");
        code.AppendLine($"    public override {root} Rebuild({root} root, global::LinQL.Caching.IEntityReader reader)");
        code.AppendLine("    {");

        var rebuilt = new List<(string Property, string Expression)>();

        foreach (var field in fields)
        {
            var property = SchemaNames.FieldName(field.Definition.Name.Value);
            var expression = this.Value($"root.{property}", field, rebuild: true, "reader", "r", code, 2);

            rebuilt.Add((property, expression));
        }

        code.AppendLine();
        code.Append($"        return new {root}").AppendLine();
        code.AppendLine("        {");

        foreach (var definition in this.model.FieldsOf(rootTypeName))
        {
            var property = SchemaNames.FieldName(definition.Name.Value);
            var (_, expression) = rebuilt.FirstOrDefault(x => x.Property == property);

            code.AppendLine($"            {property} = {expression ?? $"root.{property}"},");
        }

        code.AppendLine("        };");
        code.AppendLine("    }");
        code.AppendLine("}");

        return code.ToString();
    }

    /// <summary>
    /// Emits the code that normalises one selected field's value.
    /// </summary>
    /// <param name="expression">The expression holding the value.</param>
    /// <param name="field">The selected field.</param>
    /// <param name="rebuild">Whether the code rebuilds rather than writes.</param>
    /// <param name="accessor">The writer or reader parameter's name.</param>
    /// <param name="prefix">The prefix of the locals emitted here.</param>
    /// <param name="code">The code being emitted.</param>
    /// <param name="depth">The indentation depth.</param>
    /// <returns>The expression holding the result, which is the input when nothing needs normalising.</returns>
    private string Value(
        string expression,
        CacheField field,
        bool rebuild,
        string accessor,
        string prefix,
        StringBuilder code,
        int depth)
        => this.Value(expression, field.Definition.Type.NamedType().Name.Value, field.Children, field.IsList, rebuild, accessor, prefix, code, depth);

    private string Value(
        string expression,
        string typeName,
        IReadOnlyList<CacheField> children,
        bool isList,
        bool rebuild,
        string accessor,
        string prefix,
        StringBuilder code,
        int depth)
    {
        // A subtree with no entity in it needs no normalising: its fields are copied with the type that holds
        // them, and the store never sees them.
        if (!this.ContainsEntities(typeName, children))
        {
            return expression;
        }

        var value = this.Next(prefix);

        Line(code, depth, $"var {value} = {expression};");
        Line(code, depth, $"if ({value} is not null)");
        Line(code, depth, "{");

        if (isList)
        {
            var itemName = typeName;
            var list = this.Next(prefix);

            Line(code, depth + 1, $"var {list} = new global::{this.clientNamespace}.{itemName}[{value}.Length];");
            Line(code, depth + 1, $"for (var i{list} = 0; i{list} < {value}.Length; i{list}++)");
            Line(code, depth + 1, "{");

            var item = this.Value($"{value}[i{list}]", typeName, children, isList: false, rebuild, accessor, prefix, code, depth + 2);

            Line(code, depth + 2, $"{list}[i{list}] = {item};");
            Line(code, depth + 1, "}");
            Line(code, depth + 1, $"{value} = {list};");
        }
        else
        {
            this.Node(value, typeName, children, rebuild, accessor, prefix, code, depth + 1);
        }

        Line(code, depth, "}");
        code.AppendLine();

        return value;
    }

    /// <summary>
    /// Emits the code that writes or rebuilds one object value.
    /// </summary>
    /// <param name="value">The local holding the value, which is assigned the result.</param>
    /// <param name="typeName">The type's name.</param>
    /// <param name="children">The fields the selection covers.</param>
    /// <param name="rebuild">Whether the code rebuilds rather than writes.</param>
    /// <param name="accessor">The writer or reader parameter's name.</param>
    /// <param name="prefix">The prefix of the locals emitted here.</param>
    /// <param name="code">The code being emitted.</param>
    /// <param name="depth">The indentation depth.</param>
    private void Node(
        string value,
        string typeName,
        IReadOnlyList<CacheField> children,
        bool rebuild,
        string accessor,
        string prefix,
        StringBuilder code,
        int depth)
    {
        var type = $"global::{this.clientNamespace}.{typeName}";
        var key = this.model.EntityKeys.TryGetValue(typeName, out var entityKey) ? entityKey : null;
        string? existing = null;

        if (rebuild)
        {
            if (key is not null)
            {
                var stored = this.Next(prefix);
                var current = this.Next(prefix);

                Line(code, depth, $"if ({accessor}.TryRead(\"{typeName}\", {value}.{SchemaNames.FieldName(key.FieldName)}!, out var {stored}) && {stored} is {type} {current})");
                Line(code, depth, "{");
                Line(code, depth + 1, $"{value} = {current};");
                Line(code, depth, "}");
            }
        }
        else if (key is not null)
        {
            var stored = this.Next(prefix);

            existing = this.Next(prefix);

            Line(code, depth, $"{accessor}.TryRead(\"{typeName}\", {value}.{SchemaNames.FieldName(key.FieldName)}!, out var {stored});");
            Line(code, depth, $"var {existing} = {stored} as {type};");
        }

        var written = new List<(string Property, string Expression)>();

        // A child is normalised before the object that holds it, so an entity nested in a selection is stored
        // before the instance referring to it.
        foreach (var child in children)
        {
            var property = SchemaNames.FieldName(child.Definition.Name.Value);
            var childType = child.Definition.Type.NamedType().Name.Value;

            if (!this.ContainsEntities(childType, child.Children))
            {
                continue;
            }

            written.Add((property, this.Value($"{value}.{property}", childType, child.Children, child.IsList, rebuild, accessor, prefix, code, depth)));
        }

        // A rebuild only has to copy an instance when a nested entity changed under it, and a first write
        // already holds defaults for everything it did not select, so both keep the original reference when
        // there is nothing to merge. That keeps a stored instance identical to the one the response produced.
        var needed = rebuild ? written.Count > 0 : existing is not null || written.Count > 0;

        if (!needed)
        {
            if (!rebuild && key is not null)
            {
                Line(code, depth, $"{accessor}.Write(\"{typeName}\", {value}.{SchemaNames.FieldName(key.FieldName)}!, {value});");
            }

            return;
        }

        Line(code, depth, $"{value} = new {type}");
        Line(code, depth, "{");

        foreach (var definition in this.model.FieldsOf(typeName))
        {
            var property = SchemaNames.FieldName(definition.Name.Value);
            var (_, childExpression) = written.FirstOrDefault(x => x.Property == property);

            string expression;

            if (childExpression is not null)
            {
                expression = childExpression;
            }
            else if (existing is not null && !children.Any(x => SchemaNames.FieldName(x.Definition.Name.Value) == property))
            {
                // The write did not select this property, so what the store holds is still the truth. A first
                // write has nothing to carry forward, and fills it with default! as a generated client does.
                expression = $"{existing}?.{property} ?? default!";
            }
            else
            {
                expression = $"{value}.{property}";
            }

            Line(code, depth + 1, $"{property} = {expression},");
        }

        Line(code, depth, "};");

        if (!rebuild && key is not null)
        {
            Line(code, depth, $"{accessor}.Write(\"{typeName}\", {value}.{SchemaNames.FieldName(key.FieldName)}!, {value});");
        }
    }

    /// <summary>
    /// Gets whether a selection normalises anything: an entity at this node, or one below it.
    /// </summary>
    /// <param name="typeName">The type's name.</param>
    /// <param name="children">The fields the selection covers.</param>
    /// <returns><see langword="true"/> when the subtree holds an entity.</returns>
    private bool ContainsEntities(string typeName, IReadOnlyList<CacheField> children)
        => this.model.EntityKeys.ContainsKey(typeName)
            || children.Any(x => this.ContainsEntities(x.Definition.Type.NamedType().Name.Value, x.Children));

    private string Next(string prefix) => prefix + this.locals++.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void Line(StringBuilder code, int depth, string text)
        => code.Append(' ', depth * 4).AppendLine(text);

    /// <summary>
    /// One selected field, and the fields its selection covers.
    /// </summary>
    /// <param name="Definition">The schema's field.</param>
    /// <param name="Children">The fields its selection covers, empty for a leaf.</param>
    /// <param name="IsList">Whether the field's type is a list.</param>
    private sealed record CacheField(FieldDefinitionNode Definition, IReadOnlyList<CacheField> Children, bool IsList);
}
