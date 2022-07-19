namespace LinQL;

using System.Text.Json;
using Microsoft.Extensions.Options;

/// <summary>
/// Configuration for a given <see cref="Graph"/>.
/// </summary>
public class GraphOptions
{
    /// <summary>
    /// Gets or sets the seriazation options to use.
    /// </summary>
    public JsonSerializerOptions Serializer { get; set; } = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// Gets or sets the connection to use.
    /// </summary>
    public Func<IGraphQLConnection>? Connection { get; set; }

    /// <summary>
    /// Gets or sets the subscription connection to use.
    /// </summary>
    public Func<IGraphQLSubscriptionConnection>? SubscriptionConnection { get; set; }

    /// <summary>
    /// Gets or sets the subscription connection to use if configured.
    /// </summary>
    public IGraphQLSubscriptionConnection GetSubscriptionConnection()
        => (this.SubscriptionConnection ?? throw new InvalidOperationException("No subscription connection configured"))();
}

/// <summary>
/// Graph options for an explicit type.
/// </summary>
/// <typeparam name="TGraph"></typeparam>
public class GraphOptions<TGraph> : GraphOptions
{
    /// <summary>
    /// Validator of <see cref="GraphOptions{TGraph}"/>.
    /// </summary>
    public class Validator : IValidateOptions<GraphOptions<TGraph>>
    {
        /// <inheritdoc/>
        public ValidateOptionsResult Validate(string name, GraphOptions<TGraph> options)
        {
            if (options.Connection is null)
            {
                return ValidateOptionsResult.Fail("No GraphQL Connection specified");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
