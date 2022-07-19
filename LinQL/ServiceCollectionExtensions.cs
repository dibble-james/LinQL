namespace LinQL;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// 
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configure a <see cref="Graph"/> into the DI container.
    /// </summary>
    /// <typeparam name="TGraph">The type of the graph.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>Graph client configuration builder.</returns>
    public static GraphOptionsBuilder<TGraph> AddGraphQLClient<TGraph>(this IServiceCollection services)
        where TGraph : Graph => new(services);
}
