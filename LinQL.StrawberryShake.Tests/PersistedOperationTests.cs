namespace LinQL.StrawberryShake.Tests;

using System.IO;
using FluentAssertions;
using LinQL.Generated;
using Xunit;

/// <summary>
/// Checks the generated persisted operation registry, and the files the build writes from it, against the
/// binders the registry was generated with.
/// </summary>
public class PersistedOperationTests
{
    [Fact]
    public void EveryDeclaredQueryIsRegisteredUnderItsGeneratedId()
    {
        var query = CachingQueriesFilmById.FilmById("1");

        PersistedQueries.Documents[query.Id].Should().Be(query.Document);
    }

    [Fact]
    public void ADeclaredQueryIdIsTheHashOfItsDocument()
    {
        var query = CachingQueriesFilmById.FilmById("1");

        query.Id.Should().Be(PersistedQueryId.Compute(query.Document));
    }

    [Fact]
    public void TheBuildWritesTheDocumentsWhereAServerReadsThem()
    {
        var query = CachingQueriesFilmById.FilmById("1");
        var file = Path.Combine(ProjectDirectory(), "obj", "persisted-operations", query.Id + ".graphql");

        // The file holds the document alone, so a server hashes the same text the id was computed from.
        File.Exists(file).Should().BeTrue();
        File.ReadAllText(file).Should().Be(query.Document);
    }

    /// <summary>
    /// Finds the project directory from the test binaries, which sit three levels below it.
    /// </summary>
    /// <returns>The project directory.</returns>
    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "LinQL.StrawberryShake.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The test binary is not inside its project directory.");
    }
}
