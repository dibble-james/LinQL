namespace LinQL.Tests;
using System;
using System.Threading.Tasks;

using LinQL.Description;
using LinQL.Translation;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class SubscriptionTests : IDisposable
{
    private readonly TestServer server;

    public SubscriptionTests() => this.server = new TestServerApplicationFactory<SimpleStartup>().Server;

    [Fact]
    public async Task TestSubscriptions()
    {
        this.server.CreateClient();
        var ws = this.server.CreateWebSocketClient();

        var client = new ServiceCollection().AddGraphQLClient<SubscriptionGraph>()
           .WithHttpConnection(opt => opt = this.server.CreateClient())
           .WithWebSocketConnection(
               new UriBuilder(this.server.BaseAddress) { Scheme = "ws", Path = "graphql" }.Uri,
               opt =>
               {
                   ws.ConfigureRequest = opt;
                   return (u, ct) => ws.ConnectAsync(u, ct);
               })
           .Services.BuildServiceProvider()
           .GetRequiredService<SubscriptionGraph>();

        var cts = new CancellationTokenSource();
        cts.CancelAfter(5000);

        var ranToCompletion = false;

        var handle = await client.Subscription.Select(x => x.GetNumbers().SelectAll())
            .Subscribe(async (number, _) =>
            {
                await Task.Yield();
                if (number.Number > 5)
                {
                    ranToCompletion = true;
                    cts.Cancel(false);
                }
            });

        while (!cts.IsCancellationRequested)
        {
            await Task.Delay(500);
        }

        ranToCompletion.Should().BeTrue();
    }

    public class SimpleStartup
    {
        public void ConfigureServices(IServiceCollection services)
            => services.AddGraphQLServer().AddQueryType<TestQuery>().AddSubscriptionType<TestSubscriptionType>();

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseWebSockets();
            app.UseEndpoints(e => e.MapGraphQL());
        }
    }

    public class TestServerApplicationFactory<TStartup> : WebApplicationFactory<TStartup>
        where TStartup : class
    {
        protected override TestServer CreateServer(IWebHostBuilder builder) =>
            base.CreateServer(
                builder.UseSolutionRelativeContentRoot(""));

        protected override IWebHostBuilder CreateWebHostBuilder() =>
            WebHost.CreateDefaultBuilder()
                .UseStartup<TStartup>();
    }

    private class SubscriptionGraph : Graph
    {
        public SubscriptionGraph(GraphOptions<SubscriptionGraph> options)
            : base(new Logger<SubscriptionGraph>(new NullLoggerFactory()), options, new TranslationProvider())
        {
        }

        public RootType<TestSubscription> Subscription => this.RootType<TestSubscription>();
    }

    public class TestQuery
    {
        public string NotARealQuery() => string.Empty;
    }

    public class TestSubscriptionType
    {
        [SubscribeAndResolve]
        public async IAsyncEnumerable<TestSubscription.NumberResult> Numbers()
        {
            var i = 0;

            while (true)
            {
                await Task.Delay(100);
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
        this.server.Dispose();
    }
}
