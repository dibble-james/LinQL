namespace LinQL.StrawberryShake.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using global::StrawberryShake;
using global::StrawberryShake.Transport.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The behavioural proof of entity-level caching for declared queries.
/// </summary>
/// <remarks>
/// Every test runs against the same Hot Chocolate fixture the other integration tests use, with the client the
/// LinQL generator compiled from <c>SDLs/Caching.graphql</c> and its <c>@key</c> companion. Nothing here reads
/// the entity store: what is asserted is what a user observes through <c>SendAsync</c> and <c>Watch</c>, which is
/// the acceptance criterion the ADR names.
/// </remarks>
public class EntityCacheTests : IDisposable
{
    private readonly WebApplication server;
    private readonly StrawberryShakeLinQLClient client;

    public EntityCacheTests()
    {
        Server.CachingData.Reset();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting().AddGraphQLServer().AddQueryType<Server.Query>().AddMutationType<Server.Mutation>();

        this.server = builder.Build();
        this.server.UseRouting().UseEndpoints(endpoints => endpoints.MapGraphQL());
        this.server.StartAsync().GetAwaiter().GetResult();

        var testServer = this.server.GetTestServer();
        var address = new Uri(testServer.BaseAddress, "/graphql");

        // A request's response stream disposes the client it was sent through, so each request gets its own.
        // The tests send more than one request through a single LinQL client, which a cached watch makes
        // unavoidable: replaying a cached result completes that request's stream before the next one is sent.
        this.client = new HttpConnection(() => new HttpClient(testServer.CreateHandler())
        {
            BaseAddress = address,
        }).WithLinQL(options => options.Strategy = ExecutionStrategy.CacheFirst);
    }

    [Fact]
    public async Task MutationRefreshesACachedDeclaration()
    {
        var query = CachingQueriesFilmById.FilmById("1");
        var sent = await this.client.SendAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        sent.Data.Should().NotBeNull();
        sent.Data!.Title.Should().Be("A New Hope");

        var values = new Recorder<Cached.Film?>();
        using (this.client.Watch(query, cancellationToken: TestContext.Current.CancellationToken).Subscribe(values))
        {
            await values.WaitForAsync(1);
            values.Results[0].Data!.Title.Should().Be("A New Hope", "the watch replays the cached result");

            await this.client.SendAsync(
                CachingQueriesSetTitleById.SetTitleById("1", "Renamed"),
                cancellationToken: TestContext.Current.CancellationToken);

            await values.WaitForAsync(2);
            values.Results[1].Data!.Title.Should().Be("Renamed", "the mutation rewrote the film the query is watching");
        }
    }

    [Fact]
    public async Task PartialWriteCopiesUnselectedPropertiesForward()
    {
        var query = CachingQueriesFilmById.FilmById("1");
        await this.client.SendAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        var values = new Recorder<Cached.Film?>();
        using (this.client.Watch(query, cancellationToken: TestContext.Current.CancellationToken).Subscribe(values))
        {
            await values.WaitForAsync(1);

            // The mutation selects the id and the title only, so the stored episode has to survive the write.
            await this.client.SendAsync(
                CachingQueriesSetTitleById.SetTitleById("1", "Renamed"),
                cancellationToken: TestContext.Current.CancellationToken);

            await values.WaitForAsync(2);
            values.Results[1].Data!.Title.Should().Be("Renamed");
            values.Results[1].Data!.Episode.Should().Be(4, "the write did not select the episode, so the stored one is carried forward");
        }
    }

    [Fact]
    public async Task RebuildKeepsTheErrorsTheResultWasBuiltWith()
    {
        var query = CachingQueriesFilmAndBoom.FilmAndBoom("1");
        var sent = await this.client.SendAsync(query, cancellationToken: TestContext.Current.CancellationToken);

        sent.HasErrors.Should().BeTrue();

        var values = new Recorder<object>();
        using (this.client.Watch(query, cancellationToken: TestContext.Current.CancellationToken).Subscribe(values))
        {
            await values.WaitForAsync(1);

            await this.client.SendAsync(
                CachingQueriesSetTitleById.SetTitleById("1", "Renamed"),
                cancellationToken: TestContext.Current.CancellationToken);

            await values.WaitForAsync(2);

            var film = values.Results[1].Data.Member("Film") as Cached.Film;

            film.Should().NotBeNull();
            film.Title.Should().Be("Renamed");
            values.Results[1].Errors.Should().ContainSingle("a rebuild replaces data only, as a generated client's does");
            values.Results[1].Errors[0].Message.Should().Be("boom");
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.client.Dispose();
        this.server.StopAsync().GetAwaiter().GetResult();
        this.server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    // Collects the results a watch emits.
    private sealed class Recorder<T> : IObserver<LinQLResult<T>>
    {
        private readonly Lock sync = new();
        private readonly List<LinQLResult<T>> results = [];

        public LinQLResult<T>[] Results
        {
            get
            {
                lock (this.sync)
                {
                    return [.. this.results];
                }
            }
        }

        public Exception? Error { get; private set; }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => this.Error = error;

        public void OnNext(LinQLResult<T> value)
        {
            lock (this.sync)
            {
                this.results.Add(value);
            }
        }

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (this.Results.Length < count)
            {
                if (this.Error is { } error)
                {
                    throw error;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"Expected {count} result(s) but received {this.Results.Length}.");
                }

                await Task.Delay(10);
            }
        }
    }
}

// Reads a property of a result an anonymous projection produced.
internal static class AnonymousResult
{
    /// <summary>
    /// Reads one property.
    /// </summary>
    /// <param name="data">The projected data.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The property's value.</returns>
    public static object? Member(this object? data, string name)
        => data?.GetType().GetProperty(name)?.GetValue(data);
}
