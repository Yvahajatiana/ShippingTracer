using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Contracts;
using ParcelFlow.BuildingBlocks.Messaging;
using Xunit;

namespace ParcelFlow.BuildingBlocks.Tests;

public sealed class MessageConventionsTests
{
    [Fact]
    public async Task Published_integration_events_use_their_envelope_as_transport_identity()
    {
        await using var provider = CreateProvider();
        var harness = await StartAsync(provider);
        var message = Ping.Create();

        await harness.Bus.Publish(message);

        var published = (await harness.Published.SelectAsync<Ping>().First()).Context;
        published.MessageId.Should().Be(message.EventId);
        published.CorrelationId.Should().Be(message.CorrelationId);
    }

    [Fact]
    public async Task Sent_integration_commands_use_their_envelope_as_transport_identity()
    {
        await using var provider = CreateProvider();
        var harness = await StartAsync(provider);
        var message = Ping.Create();

        var endpoint = await harness.Bus.GetSendEndpoint(new Uri("queue:ping-commands"));
        await endpoint.Send(message);

        var sent = (await harness.Sent.SelectAsync<Ping>().First()).Context;
        sent.MessageId.Should().Be(message.EventId);
        sent.CorrelationId.Should().Be(message.CorrelationId);
    }

    [Fact]
    public async Task Events_published_by_a_consumer_carry_the_consumed_correlation()
    {
        await using var provider = CreateProvider();
        var harness = await StartAsync(provider);
        var message = Ping.Create();

        await harness.Bus.Publish(message);

        var relayed = (await harness.Published.SelectAsync<Pong>().First()).Context;
        relayed.Message.CorrelationId.Should().Be(message.CorrelationId);
        relayed.Message.SourceEventId.Should().Be(message.EventId);
        relayed.CorrelationId.Should().Be(message.CorrelationId);
        relayed.MessageId.Should().Be(relayed.Message.EventId);
    }

    [Fact]
    public async Task An_explicit_message_id_set_by_the_caller_is_preserved()
    {
        await using var provider = CreateProvider();
        var harness = await StartAsync(provider);
        var message = Ping.Create();
        var transportId = Guid.NewGuid();

        await harness.Bus.Publish(message, context => context.MessageId = transportId);

        (await harness.Published.SelectAsync<Ping>().First()).Context.MessageId.Should().Be(transportId);
    }

    [Fact]
    public async Task Messages_without_an_envelope_are_left_untouched()
    {
        await using var provider = CreateProvider();
        var harness = await StartAsync(provider);

        await harness.Bus.Publish(new Notice("hello"));

        var published = (await harness.Published.SelectAsync<Notice>().First()).Context;
        published.MessageId.Should().NotBeNull().And.NotBe(Guid.Empty);
        published.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void Every_shared_contract_carries_the_integration_event_envelope()
    {
        var contracts = typeof(IntegrationEvent).Assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Namespace!.EndsWith(".V1", StringComparison.Ordinal))
            .ToArray();

        contracts.Should().NotBeEmpty();
        contracts.Should().AllSatisfy(type => type.Should().BeAssignableTo<IntegrationEvent>());
    }

    private static ServiceProvider CreateProvider() => new ServiceCollection()
        .AddParcelFlowCorrelation()
        .AddMassTransitTestHarness(bus =>
        {
            bus.AddConsumer<RelayConsumer>();
            bus.UsingInMemory((context, configurator) =>
            {
                configurator.UseParcelFlowMessageConventions(context);
                configurator.ConfigureEndpoints(context);
            });
        })
        .BuildServiceProvider(true);

    private static async Task<ITestHarness> StartAsync(IServiceProvider provider)
    {
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return harness;
    }

    public sealed record Ping(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc)
        : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc)
    {
        public static Ping Create() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    public sealed record Pong(Guid EventId, Guid CorrelationId, DateTimeOffset OccurredAtUtc, Guid SourceEventId)
        : IntegrationEvent(EventId, CorrelationId, OccurredAtUtc);

    public sealed record Notice(string Text);

    public sealed class RelayConsumer(ICorrelationContext correlation) : IConsumer<Ping>
    {
        // Mirrors the service handlers: the published event takes its CorrelationId from the ambient context.
        public Task Consume(ConsumeContext<Ping> context) => context.Publish(new Pong(Guid.NewGuid(),
            correlation.CorrelationId, DateTimeOffset.UtcNow, context.Message.EventId));
    }
}
