using System.Collections.Concurrent;
using System.Net.Http.Json;
using BerryExchange.Api.Infrastructure.Messaging;
using BerryExchange.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BerryExchange.Api.Tests;

public sealed class RecordingEventPublisher : IEventPublisher
{
    public ConcurrentQueue<(string RoutingKey, object Event)> Published { get; } = new();
    public Task PublishAsync<T>(string routingKey, T @event, CancellationToken ct)
    {
        Published.Enqueue((routingKey, @event!));
        return Task.CompletedTask;
    }
}

public class ListingEventPublishingTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;
    public ListingEventPublishingTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Creating_a_listing_publishes_ListingCreatedEvent()
    {
        var recorder = new RecordingEventPublisher();
        var client = _fixture.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(recorder);
        })).CreateClient();

        var email = $"grower-{Guid.NewGuid():N}@test.dev";
        var register = await client.PostAsJsonAsync("/api/accounts/register",
            new { Email = email, Password = "Password1!", DisplayName = "Grower" });
        register.EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync("/api/listings",
            new { BerryType = "Strawberry", FarmName = "Sunny Acres", PricePerKg = 6.5m, QuantityAvailableKg = 10m, Note = "sweet" });
        create.EnsureSuccessStatusCode();

        var (routingKey, evt) = Assert.Single(recorder.Published);
        Assert.Equal(ListingCreatedEvent.RoutingKey, routingKey);
        var typed = Assert.IsType<ListingCreatedEvent>(evt);
        Assert.Equal("Strawberry", typed.BerryType);
        Assert.Equal(10, typed.QuantityAvailableKg);
    }
}
