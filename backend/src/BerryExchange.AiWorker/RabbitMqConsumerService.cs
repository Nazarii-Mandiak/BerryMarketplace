using System.Text.Json;
using BerryExchange.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BerryExchange.AiWorker;

public sealed class RabbitMqConsumerService : BackgroundService
{
    public const string QueueName = "ai-enrichment";
    public const string DeadLetterExchange = "berry.events.dlx";
    public const string DeadLetterQueue = "ai-enrichment.dead";

    private readonly IConfiguration _config;
    private readonly IListingCreatedHandler _handler;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly CancellationTokenSource _stoppingCts = new();
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(IConfiguration config, IListingCreatedHandler handler,
        ILogger<RabbitMqConsumerService> logger)
    {
        _config = config;
        _handler = handler;
        _logger = logger;
    }

    // Connects, declares the exchange/queue/DLQ topology, and registers the
    // consumer here (awaited) rather than in ExecuteAsync. BackgroundService's
    // default StartAsync fires ExecuteAsync and returns as soon as it hits its
    // first await, without waiting for setup to finish. That raced any caller
    // that publishes immediately after StartAsync returns (the Testcontainers
    // integration test does exactly this): the event could be published to the
    // topic exchange before the ai-enrichment queue was declared and bound,
    // and get silently dropped (topic exchanges drop unroutable messages).
    // Awaiting the full setup here closes that window — by the time StartAsync
    // returns, the queue exists, is bound, and BasicConsumeAsync has registered
    // the consumer with the broker.
    //
    // The retry-loop/topology/handler logic below is unchanged from a
    // straightforward ExecuteAsync-based implementation; only where it runs
    // (StartAsync vs. ExecuteAsync) and the CancellationToken it uses moved.
    // A dedicated long-lived _stoppingCts (rather than the CancellationToken
    // passed into StartAsync, which the generic host may dispose once all
    // hosted services have started) is used for anything that must remain
    // valid for the service's whole lifetime, i.e. the consumer callback.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var ct = _stoppingCts.Token;
        var factory = new ConnectionFactory
        {
            HostName = _config["RabbitMq:Host"] ?? "localhost",
            Port = int.TryParse(_config["RabbitMq:Port"], out var p) ? p : 5672,
            UserName = _config["RabbitMq:Username"] ?? "guest",
            Password = _config["RabbitMq:Password"] ?? "guest",
        };

        // Startup retry: in compose/k8s the broker may come up after the worker.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ not reachable yet; retrying in 3s");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
        if (_connection is null) return;

        _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
        await _channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout,
            durable: true, autoDelete: false, cancellationToken: ct);
        await _channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false,
            autoDelete: false, cancellationToken: ct);
        await _channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, routingKey: "",
            cancellationToken: ct);
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange },
            cancellationToken: ct);
        await _channel.QueueBindAsync(QueueName, MessagingConventions.Exchange,
            ListingCreatedEvent.RoutingKey, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var evt = JsonSerializer.Deserialize<ListingCreatedEvent>(ea.Body.Span)
                    ?? throw new JsonException("null event payload");
                await _handler.HandleAsync(evt, ct);
                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process listing.created; dead-lettering");
                // requeue:false routes to the DLQ via x-dead-letter-exchange (single
                // delivery attempt; bounded-retry-by-DLQ documented in ADR-0009).
                await _channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            }
        };
        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer, ct);

        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Delay(Timeout.Infinite, _stoppingCts.Token).ContinueWith(_ => { }, CancellationToken.None);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stoppingCts.CancelAsync();
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        await base.StopAsync(cancellationToken);
        _stoppingCts.Dispose();
    }
}
