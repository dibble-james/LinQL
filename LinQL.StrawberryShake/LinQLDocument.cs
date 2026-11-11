namespace LinQL.StrawberryShake;

using System.Security.Cryptography;
using System.Text;
using LinQL.Description;
using global::StrawberryShake;

/// <summary>
/// A Strawberry Shake document built from a translated LinQL query.
/// </summary>
internal sealed class LinQLDocument : IDocument
{
    private readonly byte[] body;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinQLDocument"/> class.
    /// </summary>
    /// <param name="operationType">The root operation type.</param>
    /// <param name="name">The operation name declared by the document.</param>
    /// <param name="query">The GraphQL query body.</param>
    public LinQLDocument(RootOperationType operationType, string name, string query)
    {
        this.Kind = operationType switch
        {
            RootOperationType.Query => OperationKind.Query,
            RootOperationType.Mutation => OperationKind.Mutation,
            RootOperationType.Subscription => OperationKind.Subscription,
            _ => throw new NotSupportedException($"Unsupported root operation {operationType}"),
        };

        this.Name = name;
        this.body = Encoding.UTF8.GetBytes(query);
        this.Hash = new DocumentHash("sha256", Convert.ToBase64String(SHA256.HashData(this.body)));
    }

    /// <summary>
    /// Gets the operation name declared by the document.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc />
    public OperationKind Kind { get; }

    /// <inheritdoc />
    public ReadOnlySpan<byte> Body => this.body;

    /// <inheritdoc />
    public DocumentHash Hash { get; }

    /// <summary>
    /// Gets the GraphQL query body.
    /// </summary>
    public string Query => Encoding.UTF8.GetString(this.body);

    /// <summary>
    /// Ensures the translated LinQL query declares an operation name.
    /// </summary>
    /// <remarks>
    /// LinQL emits <c>query { ... }</c> when a query has no variables and <c>query linql($var1: T) { ... }</c>
    /// when it does. The transport sends the request name as the operation name, so an anonymous document is
    /// rewritten to declare the <c>linql</c> operation and the matching name is returned.
    /// </remarks>
    /// <param name="query">The translated query.</param>
    /// <returns>The normalized query and the operation name it declares.</returns>
    public static (string Query, string Name) Normalize(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var index = 0;
        while (index < query.Length && char.IsWhiteSpace(query[index]))
        {
            index++;
        }

        var keywordEnd = index;
        while (keywordEnd < query.Length && !char.IsWhiteSpace(query[keywordEnd]) && query[keywordEnd] != '{')
        {
            keywordEnd++;
        }

        var rest = query[keywordEnd..].TrimStart();

        if (rest.Length > 0 && rest[0] != '{')
        {
            var nameEnd = 0;
            while (nameEnd < rest.Length && (char.IsLetterOrDigit(rest[nameEnd]) || rest[nameEnd] == '_'))
            {
                nameEnd++;
            }

            var name = rest[..nameEnd];

            return name.Length == 0 ? (query, "linql") : (query, name);
        }

        return (string.Concat(query[..keywordEnd], " linql ", rest), "linql");
    }
}
