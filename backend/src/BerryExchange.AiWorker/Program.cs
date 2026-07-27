using BerryExchange.AiWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<IListingCreatedHandler, LoggingListingCreatedHandler>();
builder.Services.AddHostedService<RabbitMqConsumerService>();

var host = builder.Build();
host.Run();
