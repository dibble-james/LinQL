using GraphQL.Client.Abstractions;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using LinQL;
using StarWars.Client;

var client = new GraphQLHttpClient(
    "https://swapi-graphql.netlify.app/.netlify/functions/index",
    new SystemTextJsonSerializer(new System.Text.Json.JsonSerializerOptions().WithKnownInterfaces()))
    .WithLinQL(new LinQLOptions().WithKnownScalars());

var films = await client.SendAsync((Root x) => x.ExecuteAllFilms(null, null, 10, null)!.Films!.Select(x => x.SelectAll()));

Console.WriteLine("Film Names\n=========");
Console.WriteLine("Query: {0}", films.Expression);
films!.Data.ToList().ForEach(f => Console.WriteLine(f.Title));

var firstFilm = await client.SendAsync((Root x) => x.ExecuteFilm(films!.Data.First().EpisodeID.ToString(), films!.Data.First().Id));

Console.WriteLine("\nOpening Crawl\n============");
Console.WriteLine("Query: {0}", firstFilm.Expression);
Console.WriteLine(firstFilm.Data!.OpeningCrawl);
