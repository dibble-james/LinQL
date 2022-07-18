using LinQL;
using Microsoft.Extensions.DependencyInjection;
using StarWars.Client;

var client = new ServiceCollection()
            .AddLogging()
            .AddStarWarsGraph()
            .WithHttpConnection(c => c.BaseAddress = new Uri("https://swapi-graphql.netlify.app/.netlify/functions/index"))
            .Services.BuildServiceProvider()
            .GetRequiredService<StarWarsGraph>();

var films = await client.Root.Select(x => x.ExecuteAllFilms(null, null, 10, null)!.Films!.Select(x => x.SelectAll()))
    .Execute();

Console.WriteLine("Film Names");
films!.ToList().ForEach(f => Console.WriteLine(f.Title));

Console.WriteLine("\nOpening Crawl");

var firstFilm = await client.Root.Select(x => x.ExecuteFilm(null, films!.First().Id)).ToResult();
Console.WriteLine(firstFilm.Request.Query);
