namespace LinQL;

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Computes the identifier a declared query is registered under when it is sent as a persisted operation.
/// </summary>
/// <remarks>
/// The identifier is the lowercase hexadecimal SHA-256 of the document's UTF-8 bytes. That is the id a Hot
/// Chocolate server computes when it is configured with <c>AddSha256DocumentHashProvider(HashFormat.Hex)</c>, and
/// the name its file-system operation storage looks the document up under.
/// </remarks>
public static class PersistedQueryId
{
    /// <summary>
    /// Computes the identifier of a document.
    /// </summary>
    /// <param name="document">The document, exactly as it is sent to the server.</param>
    /// <returns>The lowercase hexadecimal SHA-256 of the document's UTF-8 bytes.</returns>
    /// <exception cref="ArgumentNullException">The document is <see langword="null"/>.</exception>
    public static string Compute(string document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(document));
        var id = new StringBuilder(hash.Length * 2);

        foreach (var octet in hash)
        {
            id.Append(octet.ToString("x2", CultureInfo.InvariantCulture));
        }

        return id.ToString();
    }
}
