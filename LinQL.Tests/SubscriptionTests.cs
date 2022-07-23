namespace LinQL.Tests;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using LinQL.Description;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class SubscriptionTests : IDisposable
{
    private readonly TestServer server;

    public SubscriptionTests() => this.server = new TestServerApplicationFactory<SimpleStartup>().Server;

    [Fact(Skip = "GraphQL Client doesn't like test server")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped", Justification = "Ignore for now")]
    public async Task TestSubscriptions()
    {
        this.server.CreateClient();

        var client = new GraphQLHttpClient(
            opt =>
            {
                opt.HttpMessageHandler = this.server.CreateHandler();
                opt.EndPoint = new Uri(this.server.BaseAddress, "/graphql");
            },
            new SystemTextJsonSerializer());

        var cts = new CancellationTokenSource();
        cts.CancelAfter(5000);

        var ranToCompletion = false;

        var numbers = client.CreateSubscriptionStream((TestSubscription x) => x.GetNumbers().SelectAll());

        var lastNumer = -1;

        await foreach (var number in numbers.ToAsyncEnumerable())
        {
            number.Data.Number.Should().Be(lastNumer + 1, "Numbers should arrive in sync");

            if (number.Data.Number > 5)
            {
                ranToCompletion = true;
                break;
            }
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
