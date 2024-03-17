namespace LinQL.GraphQL.Client.Tests;

using System;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using global::GraphQL.Client.Abstractions;
using global::GraphQL.Client.Http;
using global::GraphQL.Client.Serializer.SystemTextJson;
using LinQL.Description;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

public class SubscriptionTests : IDisposable
{
    private const string HostUrl = "http://localhost:5000";
    private readonly WebApplication server;

    public SubscriptionTests()
    {
        var hostBuilder = new WebHostBuilder()
            .Configure(app =>
            {
                app.UseRouting();
                app.UseWebSockets();
                app.UseEndpoints(e => e.MapGraphQL());
            });

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting()
                .AddGraphQLServer()
                .AddQueryType<TestQuery>()
                .AddSubscriptionType<TestSubscriptionType>();

        this.server = builder.Build();
        this.server.UseRouting().UseWebSockets().UseEndpoints(e => e.MapGraphQL());
        Task.Run(() => this.server.Run(HostUrl));
    }

    [Fact]
    public async Task TestSubscriptions()
    {
        var client =
            new GraphQLHttpClient(
                opt =>
                {
                    opt.EndPoint = new UriBuilder(HostUrl) { Path = "graphql" }.Uri;
                    opt.WebSocketEndPoint = new UriBuilder(HostUrl) { Scheme = "ws", Path = "graphql" }.Uri;
                },
                new SystemTextJsonSerializer())
            .WithLinQL(new LinQLOptions());

        var ranToCompletion = false;

        var numbers = client.CreateSubscriptionStream((TestSubscription x) => x.GetNumbers().SelectAll());

        var lastNumber = -1;

        await foreach (var number in numbers.TakeUntil(Observable.Timer(TimeSpan.FromSeconds(5))).ToAsyncEnumerable())
        {
            number.Data.Number.Should().Be(lastNumber + 1, "Numbers should arrive in sync");
            lastNumber = number.Data.Number;

            if (number.Data.Number > 5)
            {
                ranToCompletion = true;
                break;
            }
        }

        ranToCompletion.Should().BeTrue();
    }

    public class TestQuery
    {
        public string NotARealQuery() => string.Empty;
    }

    public class TestSubscriptionType
    {
        [Subscribe(With = nameof(EnumerateNumbers))]
        public TestSubscription.NumberResult Numbers([EventMessage] TestSubscription.NumberResult number) => number;

        public async IAsyncEnumerable<TestSubscription.NumberResult> EnumerateNumbers([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var i = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
                yield return new TestSubscription.NumberResult { Number = i++ };
            }
        }
    }

    [OperationType(RootOperationType.Subscription)]
    public class TestSubscription : RootType<TestSubscription>
    {
        public NumberResult Numbers { get; set; }

        [GraphQLOperation, GraphQLField(Name = "numbers")]
        public NumberResult GetNumbers() => this.Numbers;

        public class NumberResult
        {
            public int Number { get; set; }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        this.server.StopAsync();
    }
}
