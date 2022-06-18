namespace LinQL.Tests;

using LinQL.Description;
using LinQL.Translation;
using Microsoft.Extensions.DependencyInjection;

public class HttpGraphQLConnectionIntegrationTests
{
    private readonly StarWarsGraph client;

    public HttpGraphQLConnectionIntegrationTests() => this.client = new ServiceCollection()
            .AddGraphQLClient<StarWarsGraph>()
            .WithHttpConnection(c => c.BaseAddress = new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index"))
            .Services.BuildServiceProvider()
            .GetRequiredService<StarWarsGraph>();

    [Fact]
    public async Task BasicQuery()
    {
        var result = await this.client.Query.Select(x => x.GetFilm("ZmlsbXM6NA==")).Execute();

        result.Id.Should().Be("ZmlsbXM6NA==");
    }

    private class StarWarsGraph : Graph
    {
        public StarWarsGraph(IGraphQLConnection connection, IQueryTranslator queryTranslator)
            : base(connection, queryTranslator)
        {
        }

        public StarWarsQueries Query => this.RootType<StarWarsQueries>();
    }

    [OperationType]
    private class StarWarsQueries : RootType<StarWarsQueries>
    {
        public Film Film { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "film")]
        public Film GetFilm(string id) => this.Film;
    }

    private class Film
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }

        [GraphQLField(Name = "episodeID")]
        public int EpisodeId { get; set; }

        public string? Title { get; set; }
    }
}
