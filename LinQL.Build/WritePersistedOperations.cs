namespace LinQL.Build;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.Build.Framework;

/// <summary>
/// Writes the persisted operation documents a built project declares, as the files an operation store reads.
/// </summary>
/// <remarks>
/// The documents are generated as constants on the <c>LinQL.Generated.PersistedQueries</c> type, so this reads
/// them from the assembly's metadata rather than by loading it: the target runs after the build, and loading the
/// output would execute its code inside MSBuild and lock the file for the rest of the build.
/// A project without declarations has no registry, so it writes nothing and only clears what an earlier build
/// left behind.
/// </remarks>
public sealed class WritePersistedOperations : Microsoft.Build.Utilities.Task
{
    private const string RegistryNamespace = "LinQL.Generated";

    private const string RegistryName = "PersistedQueries";

    private const string DocumentPrefix = "Document";

    private const string DocumentIdPrefix = "DocumentId";

    private const string Extension = ".graphql";

    private const string ManifestPrefix = "linql-";

    private const string ManifestExtension = ".manifest";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Gets or sets the path of the built assembly holding the generated registry.
    /// </summary>
    [Required]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the documents are written to.
    /// </summary>
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the project directory a relative <see cref="OutputDirectory"/> is resolved against.
    /// </summary>
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            if (!File.Exists(this.AssemblyPath))
            {
                this.Log.LogError("LinQL could not read the built assembly '{0}'.", this.AssemblyPath);
                return false;
            }

            var documents = ReadDocuments(this.AssemblyPath);

            var directory = Path.IsPathRooted(this.OutputDirectory)
                ? this.OutputDirectory
                : Path.GetFullPath(Path.Combine(this.ProjectDirectory, this.OutputDirectory));

            // The manifest sits beside the project file rather than in the output, so the directory a server reads
            // holds only operation documents and the one file is there to be reviewed or ignored.
            var manifestPath = Path.Combine(
                string.IsNullOrWhiteSpace(this.ProjectDirectory) ? directory : this.ProjectDirectory,
                ManifestPrefix + Path.GetFileNameWithoutExtension(this.AssemblyPath) + ManifestExtension);
            var previous = ReadManifest(manifestPath);
            var written = new HashSet<string>(StringComparer.Ordinal);

            if (documents.Count != 0)
            {
                Directory.CreateDirectory(directory);

                foreach (var document in documents)
                {
                    var name = document.Key + Extension;

                    // The file is the document alone, so a server hashes exactly the text the id was computed from.
                    Write(Path.Combine(directory, name), document.Value, Utf8);
                    written.Add(name);
                }

                this.Log.LogMessage(
                    MessageImportance.Normal,
                    "LinQL wrote {0} persisted operation document(s) to {1}.",
                    documents.Count,
                    directory);
            }

            this.RemoveUndeclared(directory, previous, written);
            WriteManifest(manifestPath, documents.Keys);

            return true;
        }
        catch (Exception exception)
        {
            this.Log.LogErrorFromException(exception, showStackTrace: true);
            return false;
        }
    }

    /// <summary>
    /// Reads the registered documents from an assembly's metadata.
    /// </summary>
    /// <param name="assemblyPath">The assembly.</param>
    /// <returns>The documents, by the id they are registered under.</returns>
    private static SortedDictionary<string, string> ReadDocuments(string assemblyPath)
    {
        var documents = new SortedDictionary<string, string>(StringComparer.Ordinal);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
        {
            return documents;
        }

        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);

            if (string.Equals(reader.GetString(type.Name), RegistryName, StringComparison.Ordinal)
                && string.Equals(reader.GetString(type.Namespace), RegistryNamespace, StringComparison.Ordinal))
            {
                AddRegisteredDocuments(reader, type, documents);
            }
        }

        return documents;
    }

    /// <summary>
    /// Adds one registry's documents, pairing each id constant with the document constant of the same index.
    /// </summary>
    /// <param name="reader">The assembly's metadata.</param>
    /// <param name="type">The registry type.</param>
    /// <param name="documents">The documents being collected.</param>
    private static void AddRegisteredDocuments(
        MetadataReader reader,
        TypeDefinition type,
        IDictionary<string, string> documents)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);

            if ((field.Attributes & FieldAttributes.Literal) == 0)
            {
                continue;
            }

            var name = reader.GetString(field.Name);

            // Every generated name starts with Document, so the id's prefix is tested first.
            if (name.StartsWith(DocumentIdPrefix, StringComparison.Ordinal))
            {
                ids[name.Substring(DocumentIdPrefix.Length)] = ReadConstant(reader, field);
            }
            else if (name.StartsWith(DocumentPrefix, StringComparison.Ordinal))
            {
                texts[name.Substring(DocumentPrefix.Length)] = ReadConstant(reader, field);
            }
        }

        foreach (var id in ids)
        {
            if (texts.TryGetValue(id.Key, out var document))
            {
                documents[id.Value] = document;
            }
        }
    }

    /// <summary>
    /// Reads a string constant's value.
    /// </summary>
    /// <param name="reader">The assembly's metadata.</param>
    /// <param name="field">The constant field.</param>
    /// <returns>The value, or an empty string when the field has no constant.</returns>
    private static string ReadConstant(MetadataReader reader, FieldDefinition field)
    {
        var constant = field.GetDefaultValue();

        if (constant.IsNil)
        {
            return string.Empty;
        }

        var blob = reader.GetBlobReader(reader.GetConstant(constant).Value);

        return blob.ReadUTF16(blob.Length);
    }

    /// <summary>
    /// Removes the documents an earlier build of this project wrote that the project no longer declares.
    /// </summary>
    /// <param name="directory">The output directory.</param>
    /// <param name="manifestPath">This project's manifest, which records what its last build wrote.</param>
    /// <param name="previous">The ids that build wrote.</param>
    /// <param name="written">The files this build wrote.</param>
    /// <remarks>
    /// Only a document this project wrote before is removed, so a directory shared with other projects keeps
    /// their documents, and documents written by hand are never touched. A query that two projects declare is
    /// one file, so removing it because one of them dropped it also removes it for the other until that project
    /// builds again.
    /// </remarks>
    private void RemoveUndeclared(
        string directory,
        ICollection<string> previous,
        ICollection<string> written)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var removed = 0;

        foreach (var path in Directory.GetFiles(directory, "*" + Extension, SearchOption.TopDirectoryOnly))
        {
            if (written.Contains(Path.GetFileName(path)))
            {
                continue;
            }

            if (!previous.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                continue;
            }

            File.Delete(path);
            removed++;

            this.Log.LogMessage(
                MessageImportance.Low,
                "LinQL removed '{0}', whose declared query no longer exists.",
                path);
        }

        if (removed != 0)
        {
            this.Log.LogMessage(
                MessageImportance.Normal,
                "LinQL removed {0} persisted operation document(s) that are no longer declared.",
                removed);
        }
    }

    /// <summary>
    /// Reads the ids a manifest records.
    /// </summary>
    /// <param name="path">The manifest.</param>
    /// <returns>The ids, which is nothing when the file does not exist.</returns>
    private static HashSet<string> ReadManifest(string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return ids;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var id = line.Trim();

            if (IsId(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// Records the ids this build wrote, so a later build can tell which documents are its own.
    /// </summary>
    /// <param name="path">The manifest.</param>
    /// <param name="ids">The ids this build wrote.</param>
    /// <remarks>
    /// A project that declares nothing has no manifest, which is what its next build reads as "wrote nothing",
    /// so the documents of a project that stopped declaring queries are not left claimed by anyone.
    /// </remarks>
    private static void WriteManifest(string path, ICollection<string> ids)
    {
        if (ids.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Write(path, string.Join("\n", ids) + "\n", Utf8);
    }

    /// <summary>
    /// Tells whether a file name is a document id, which is what this target names its files.
    /// </summary>
    /// <param name="value">The file name without its extension.</param>
    /// <returns><see langword="true"/> when the name is a lowercase hexadecimal SHA-256 digest.</returns>
    private static bool IsId(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Writes a file, leaving an identical one alone.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="content">The content.</param>
    /// <param name="encoding">The encoding.</param>
    /// <remarks>
    /// A project that builds several target frameworks runs the target once per framework, so the same content can
    /// arrive twice; skipping an identical file keeps the second write from touching it.
    /// </remarks>
    private static void Write(string path, string content, Encoding encoding)
    {
        if (File.Exists(path) && string.Equals(File.ReadAllText(path), content, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(path, content, encoding);
    }
}
