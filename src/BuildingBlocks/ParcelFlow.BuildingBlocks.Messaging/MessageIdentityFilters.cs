using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Contracts;

namespace ParcelFlow.BuildingBlocks.Messaging;

/// <summary>Copies the envelope of an integration event onto the transport headers.</summary>
public static class IntegrationEventEnvelope
{
    /// <summary>
    /// Sets MessageId to EventId, so the MassTransit Inbox and the consumers deduplicate on the same identifier,
    /// and CorrelationId to the event CorrelationId. Other messages are left untouched.
    /// </summary>
    public static void Apply<T>(SendContext<T> context) where T : class
    {
        if (context.Message is not IntegrationEvent integrationEvent) return;
        if (integrationEvent.EventId != Guid.Empty) context.MessageId = integrationEvent.EventId;
        if (integrationEvent.CorrelationId != Guid.Empty) context.CorrelationId = integrationEvent.CorrelationId;
    }
}

/// <summary>Applies the integration event envelope to every published message.</summary>
public sealed class IntegrationEventPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        IntegrationEventEnvelope.Apply(context);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventEnvelope");
}

/// <summary>Applies the integration event envelope to every sent command, such as PaymentRequested.</summary>
public sealed class IntegrationEventSendFilter<T> : IFilter<SendContext<T>> where T : class
{
    public Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        IntegrationEventEnvelope.Apply(context);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("integrationEventEnvelope");
}

/// <summary>
/// Makes the correlation of the consumed message ambient for the consumer scope, so the events published while
/// handling it carry the same CorrelationId.
/// </summary>
public sealed class CorrelationConsumeFilter<T>(CorrelationContext correlation) : IFilter<ConsumeContext<T>> where T : class
{
    public Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        // The event body is authoritative; the transport header covers messages without an envelope.
        var correlationId = context.Message is IntegrationEvent { CorrelationId: var fromEvent } && fromEvent != Guid.Empty
            ? fromEvent
            : context.CorrelationId;
        if (correlationId is { } value && value != Guid.Empty) correlation.Set(value);
        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlation");
}
