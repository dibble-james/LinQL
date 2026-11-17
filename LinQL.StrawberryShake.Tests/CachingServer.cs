namespace LinQL.StrawberryShake.Tests.Server;

using HotChocolate;

/// <summary>
/// The query root of the schema the entity-cache tests run against.
/// </summary>
/// <remarks>
/// The names here are the ones <c>SDLs/Caching.graphql</c> declares, because the generated client and this
/// code-first server have to describe the same schema for the documents the generator emits to validate.
/// </remarks>
public class Query
{
    /// <summary>
    /// Gets one film.
    /// </summary>
    /// <param name="id">The film's id.</param>
    /// <returns>The film, or <see langword="null"/> when there is none.</returns>
    public Film? GetFilm([ID] string id) => CachingData.FilmOrDefault(id);

    /// <summary>
    /// Gets every film.
    /// </summary>
    /// <returns>The films.</returns>
    public Film[] GetFilms() => CachingData.Films();

    /// <summary>
    /// Gets a value that always fails.
    /// </summary>
    /// <returns>Nothing; the resolver throws.</returns>
    /// <remarks>
    /// The field is nullable so a failure becomes an entry in <c>errors</c> beside the data the rest of the
    /// document selected, which is what the rebuild-preserves-errors test needs.
    /// </remarks>
    public string? GetBoom() => throw new GraphQLException("boom");
}

/// <summary>
/// The mutation root of the schema the entity-cache tests run against.
/// </summary>
public class Mutation
{
    /// <summary>
    /// Renames a film.
    /// </summary>
    /// <param name="id">The film's id.</param>
    /// <param name="title">The new title.</param>
    /// <returns>The renamed film.</returns>
    public Film SetFilmTitle([ID] string id, string title) => CachingData.SetTitle(id, title);
}

/// <summary>
/// A film, as the server knows it.
/// </summary>
public class Film
{
    /// <summary>
    /// Gets or sets the film's id, which is its identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the film's title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the film's episode number.
    /// </summary>
    public int Episode { get; set; }

    /// <summary>
    /// Gets or sets the film's director.
    /// </summary>
    public Person? Director { get; set; }
}

/// <summary>
/// A person, as the server knows them.
/// </summary>
public class Person
{
    /// <summary>
    /// Gets or sets the person's id, which is their identity.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the person's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The films the entity-cache tests read and change.
/// </summary>
/// <remarks>
/// The state is static so a mutation sees a film a query read. Each test resets it, so the order xunit runs
/// them in does not matter.
/// </remarks>
public static class CachingData
{
    private static readonly Lock Sync = new();
    private static Dictionary<string, Film> films = Create();

    /// <summary>
    /// Restores the films to their starting state.
    /// </summary>
    public static void Reset()
    {
        lock (Sync)
        {
            films = Create();
        }
    }

    /// <summary>
    /// Reads one film.
    /// </summary>
    /// <param name="id">The film's id.</param>
    /// <returns>The film, or <see langword="null"/> when there is none.</returns>
    public static Film? FilmOrDefault(string id)
    {
        lock (Sync)
        {
            return films.TryGetValue(id, out var film) ? film : null;
        }
    }

    /// <summary>
    /// Reads every film.
    /// </summary>
    /// <returns>The films.</returns>
    public static Film[] Films()
    {
        lock (Sync)
        {
            return [.. films.Values];
        }
    }

    /// <summary>
    /// Renames a film.
    /// </summary>
    /// <param name="id">The film's id.</param>
    /// <param name="title">The new title.</param>
    /// <returns>The renamed film.</returns>
    public static Film SetTitle(string id, string title)
    {
        lock (Sync)
        {
            var film = films[id];
            var renamed = new Film
            {
                Id = film.Id,
                Title = title,
                Episode = film.Episode,
                Director = film.Director,
            };

            films[id] = renamed;

            return renamed;
        }
    }

    private static Dictionary<string, Film> Create()
        => new(StringComparer.Ordinal)
        {
            ["1"] = new Film
            {
                Id = "1",
                Title = "A New Hope",
                Episode = 4,
                Director = new Person { Id = "p1", Name = "George Lucas" },
            },
            ["2"] = new Film
            {
                Id = "2",
                Title = "The Empire Strikes Back",
                Episode = 5,
                Director = new Person { Id = "p2", Name = "Irvin Kershner" },
            },
        };
}
