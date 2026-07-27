using BerryExchange.AiCore;
using BerryExchange.AiWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ITextEmbedder, LocalTextEmbedder>();
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = new HttpClient { BaseAddress = new Uri(config["Api:BaseUrl"] ?? "http://localhost:5091") };
    http.DefaultRequestHeaders.Add("X-Internal-ApiKey", config["Internal:ApiKey"] ?? "");
    return new EnrichmentApiClient(http);
});
builder.Services.AddSingleton<IListingCreatedHandler, EnrichingListingCreatedHandler>();
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
