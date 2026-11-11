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
    /// <param name="query">The GraphQL query body emitted by the LinQL translator.</param>
    public LinQLDocument(RootOperationType operationType, string query)
    {
        this.Kind = operationType switch
        {
            RootOperationType.Query => OperationKind.Query,
            RootOperationType.Mutation => OperationKind.Mutation,
            RootOperationType.Subscription => OperationKind.Subscription,
            _ => throw new NotSupportedException($"Unsupported root operation {operationType}"),
        };

        this.Query = query;
        this.body = Encoding.UTF8.GetBytes(query);
        this.Hash = new DocumentHash("sha256", Convert.ToBase64String(SHA256.HashData(this.body)));
    }

    /// <inheritdoc />
    public OperationKind Kind { get; }

    /// <inheritdoc />
    public ReadOnlySpan<byte> Body => this.body;

    /// <inheritdoc />
    public DocumentHash Hash { get; }

    /// <summary>
    /// Gets the GraphQL query body.
    /// </summary>
    public string Query { get; }
}
