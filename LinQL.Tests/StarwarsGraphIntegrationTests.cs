namespace LinQL.Tests;

using LinQL.Description;
using LinQL.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class StarwarsGraphIntegrationTests
{
    private readonly StarWarsGraph client;

    public StarwarsGraphIntegrationTests() => this.client = new ServiceCollection()
            .AddGraphQLClient<StarWarsGraph>()
            .WithHttpConnection(c => c.BaseAddress = new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index"))
            .Services.BuildServiceProvider()
            .GetRequiredService<StarWarsGraph>();

    [Fact]
    public async Task GetFilm()
    {
        var result = await this.client.Query.Select(x => x.GetFilm("ZmlsbXM6NA==")).Execute();

        result.Id.Should().Be("ZmlsbXM6NA==");
    }

    [Fact]
    public async Task GetAllPeople()
    {
        var result = await this.client.Query.Select(x => x.GetAllPeople(null, null, null, null).People.Select(x => x))
            .Include(p => p.AllPeople.People.Select(x => x.GetFilmsConnection(null, null, null, null).Films.Select(x => x)))
            .Execute();

        result.Any().Should().BeTrue();
    }

    private class StarWarsGraph : Graph
    {
        public StarWarsGraph(IGraphQLConnection connection, IQueryTranslator queryTranslator)
            : base(Substitute.For<ILogger<Graph>>(), connection, queryTranslator)
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

        public FilmsConnection? AllFilms { get; set; }

        [GraphQLOperation, GraphQLField(Name = "allFilms")]
        public FilmsConnection? GetAllFilms(string? after = null, int? first = null, string? before = null, int? last = null)
            => this.AllFilms;

        public Person Person { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "person")]
        public Person GetPerson(string id) => this.Person;

        public PeopleConnection? AllPeople { get; set; }

        [GraphQLOperation, GraphQLField(Name = "allPeople")]
        public PeopleConnection? GetAllPeople(string? after = null, int? first = null, string? before = null, int? last = null)
            => this.AllPeople;

        public Planet Planet { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "planet")]
        public Planet GetPlanet(string id) => this.Planet;

        public Species Species { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "species")]
        public Species GetSpecies(string id) => this.Species;

        public Starship Starship { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "starship")]
        public Starship GetStarship(string id) => this.Starship;

        public Vehicle Vehicle { get; set; } = null!;

        [GraphQLOperation, GraphQLField(Name = "vehicle")]
        public Vehicle GetVehicle(string id) => this.Vehicle;
    }

    private class Film
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }

        [GraphQLField(Name = "episodeID")]
        public int EpisodeId { get; set; }

        public string? Title { get; set; }

        public string? OpeningCrawl { get; set; }

        public DateTime? ReleaseDate { get; set; }

        public IEnumerable<string>? Producers { get; set; }
    }

    private class Person
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }

        public Planet? Homeworld { get; set; }

        public Species? Species { get; set; }

        public string? Name { get; set; }

        public string? Gender { get; set; }

        public PersonStarshipsConnection? StarshipsConnection { get; set; }

        [GraphQLOperation, GraphQLField(Name = "starshipsConnection")]
        public PersonStarshipsConnection? GetStarshipConnections(string? after = null, int? first = null, string? before = null, int? last = null)
            => this.StarshipsConnection;

        public PersonFilmsConnection? FilmConnection { get; set; }

        [GraphQLOperation, GraphQLField(Name = "filmConnection")]
        public PersonFilmsConnection? GetFilmsConnection(string? after = null, int? first = null, string? before = null, int? last = null)
            => this.FilmConnection;
    }

    private class Planet
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }

        public string? Name { get; set; }

        public IEnumerable<string>? Climates { get; set; }

        public PlanetFilmsConnection? FilmsConnection { get; set; }

        [GraphQLOperation, GraphQLField(Name = "filmsConnection")]
        public PlanetFilmsConnection? GetFilmsConnection(string? after = null, int? first = null, string? before = null, int? last = null)
            => this.FilmsConnection;
    }

    private class Species
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }
    }

    private class Starship
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? StarshipClass { get; set; }
    }

    private class Vehicle
    {
        [GraphQLField(Name = "ID")]
        public string? Id { get; set; }
    }

    private abstract class Connection<T>
    {
        public int? TotalCount { get; set; }
    }

    private class FilmsConnection : Connection<Film>
    {
        public IEnumerable<Film>? Films { get; set; }
    }

    private class PeopleConnection : Connection<Person>
    {
        public IEnumerable<Person>? People { get; set; }
    }

    private class PersonStarshipsConnection : Connection<Starship>
    {
        public IEnumerable<Starship>? Starships { get; set; }
    }

    private class PersonFilmsConnection : Connection<Film>
    {
        public IEnumerable<Film>? Films { get; set; }
    }

    private class PlanetFilmsConnection : Connection<Film>
    {
        public IEnumerable<Film>? Films { get; set; }
    }
}
