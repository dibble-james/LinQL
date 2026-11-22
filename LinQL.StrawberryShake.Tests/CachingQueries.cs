namespace LinQL.StrawberryShake.Tests;

using System.Linq.Expressions;
using LinQL.Description;
using LinQL.StrawberryShake.Tests.Cached;

// The declared queries the entity-cache tests send.
//
// Each one selects the entity's key, which is what lets the generated cache identify what it read; a declaration
// that selects an entity without its key takes no part in entity caching. The selection is written with SelectAll
// or Includes because a single object offers no projection of its own.
internal static class CachingQueries
{
    private const string Client = "LinQL.StrawberryShake.Tests.Cached";

    /// <summary>
    /// One film with every scalar field, which is the entity the mutation tests refresh.
    /// </summary>
    [LinQLQuery(Client)]
    public static readonly Expression<Func<Query, string, Film?>> FilmById =
        (q, id) => q.ExecuteFilm(id).SelectAll();

    /// <summary>
    /// One film's title and a field that always fails, so a result can carry data and errors together.
    /// </summary>
    [LinQLQuery(Client)]
    public static readonly Expression<Func<Query, string, object>> FilmAndBoom =
        (q, id) => new { q.Boom, Film = q.ExecuteFilm(id).SelectAll() };

    /// <summary>
    /// Renames a film, selecting its identity and title only so the write has to copy the rest forward.
    /// </summary>
    [LinQLQuery(Client)]
    public static readonly Expression<Func<Mutation, string, string, Film>> SetTitleById =
        (m, id, title) => m.ExecuteSetFilmTitle(id, title).Includes(f => new { f.Id, f.Title });
}
