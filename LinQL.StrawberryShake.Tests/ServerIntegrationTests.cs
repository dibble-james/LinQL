namespace LinQL.StrawberryShake.Tests;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using global::StrawberryShake.Transport.Http;
using HotChocolate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Posts LinQL operations to a real Hot Chocolate server, so the emitted document, operation name
/// and variables are validated by a server rather than replayed from a stub response.
/// </summary>
public class ServerIntegrationTests : IDisposable
{
    private readonly WebApplication server;
    private readonly HttpClient http;
    private readonly StrawberryShakeLinQLClient client;

    public ServerIntegrationTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting().AddGraphQLServer().AddQueryType<QueryType>().AddMutationType<MutationType>();

        this.server = builder.Build();
        this.server.UseRouting().UseEndpoints(endpoints => endpoints.MapGraphQL());
        this.server.StartAsync().GetAwaiter().GetResult();

        var testServer = this.server.GetTestServer();
        this.http = new HttpClient(testServer.CreateHandler())
        {
            BaseAddress = new Uri(testServer.BaseAddress, "/graphql"),
        };

        this.client = new HttpConnection(() => this.http).WithLinQL();
    }

    [Fact]
    public async Task SendsQueryToARealServer()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.GetNumber(), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(12345);
    }

    [Fact]
    public async Task SendsScalarVariablesToARealServer()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.ExecuteEchoOperation("echo"), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestObject("echo", 42));
    }

    [Fact]
    public async Task SendsIdVariablesToARealServer()
    {
        var result = await this.client.SendAsync((TestQuery q) => q.GetFilm("1"), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestFilm("Film 1", 4));
    }

    [Fact]
    public async Task SendsMutationToARealServer()
    {
        var result = await this.client.SendAsync((TestMutation m) => m.SetNumberOperation(54321), cancellationToken: TestContext.Current.CancellationToken);

        result.Errors.Should().BeEmpty();
        result.Data.Should().Be(new TestNumberResult(54321));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.client.Dispose();
        this.http.Dispose();
        this.server.StopAsync().GetAwaiter().GetResult();
        this.server.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

#pragma warning disable CA1822 // Hot Chocolate resolves these as instance members on the query and mutation types.
    public class QueryType
    {
        public int GetNumber() => 12345;

        public FilmDto GetFilm([ID] string id) => new() { Title = $"Film {id}", Episode = 4 };

        public EchoDto EchoOperation(string toEcho) => new() { Echo = toEcho, Number = 42 };
    }

    public class MutationType
    {
        public NumberDto SetNumber(int input) => new() { Number = input };
    }

    public class FilmDto
    {
        public string Title { get; set; } = string.Empty;

        public int Episode { get; set; }
    }

    public class EchoDto
    {
        public string Echo { get; set; } = string.Empty;

        public int Number { get; set; }
    }

    public class NumberDto
    {
        public int Number { get; set; }
    }
#pragma warning restore CA1822
}
