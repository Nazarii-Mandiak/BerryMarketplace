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
var workerAnthropicKey = builder.Configuration["Anthropic:ApiKey"]
    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (!string.IsNullOrEmpty(workerAnthropicKey))
{
    builder.Services.AddSingleton<IGenerativeAi>(new AnthropicGenerativeAi(workerAnthropicKey));
}
else
{
    builder.Services.AddSingleton<IGenerativeAi, DisabledGenerativeAi>();
}

builder.Services.AddSingleton<IListingCreatedHandler, EnrichingListingCreatedHandler>();
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
