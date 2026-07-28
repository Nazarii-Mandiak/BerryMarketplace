using System.Text.Json;
using BerryExchange.Contracts;
using RabbitMQ.Client;

namespace BerryExchange.Api.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqEventPublisher(IConfiguration config)
    {
        _host = config["RabbitMq:Host"] ?? throw new InvalidOperationException("Missing RabbitMq:Host");
        _port = int.TryParse(config["RabbitMq:Port"], out var p) ? p : 5672;
        _username = config["RabbitMq:Username"] ?? "guest";
        _password = config["RabbitMq:Password"] ?? "guest";
    }

    public async Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct)
    {
        var channel = await GetChannelAsync(ct);
        var body = JsonSerializer.SerializeToUtf8Bytes(@event);
        var props = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync(MessagingConventions.Exchange, routingKey,
            mandatory: false, basicProperties: props, body: body, cancellationToken: ct);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_channel is { IsOpen: true }) return _channel;
            var factory = new ConnectionFactory { HostName = _host, Port = _port, UserName = _username, Password = _password };
            _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(MessagingConventions.Exchange, ExchangeType.Topic,
                durable: true, autoDelete: false, cancellationToken: ct);
            return _channel;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _initLock.Dispose();
    }
}
