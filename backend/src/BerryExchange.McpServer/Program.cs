using BerryExchange.McpServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// stdio transport: stdout carries the MCP protocol, so every log line must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var http = new HttpClient(new HttpClientHandler { UseCookies = true })
    {
        BaseAddress = new Uri(config["BerryMcp:ApiBaseUrl"] ?? "http://localhost:5091"),
    };
    return new MarketplaceApiClient(http, config["BerryMcp:Email"], config["BerryMcp:Password"]);
});

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
