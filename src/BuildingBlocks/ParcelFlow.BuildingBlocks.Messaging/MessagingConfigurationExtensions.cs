using MassTransit;

namespace ParcelFlow.BuildingBlocks.Messaging;

public static class MessagingConfigurationExtensions
{
    /// <summary>
    /// Applies the ParcelFlow message conventions to a bus: integration events are sent and published with
    /// MessageId = EventId and CorrelationId = CorrelationId (including through the EF Core Outbox), and the
    /// correlation of every consumed message becomes the ambient <c>ICorrelationContext</c> of its consumer scope.
    /// Call it before <c>ConfigureEndpoints</c>; the service must register <c>AddParcelFlowCorrelation()</c>.
    /// </summary>
    public static void UseParcelFlowMessageConventions(this IBusFactoryConfigurator configurator, IRegistrationContext context)
    {
        configurator.UseSendFilter(typeof(IntegrationEventSendFilter<>), context);
        configurator.UsePublishFilter(typeof(IntegrationEventPublishFilter<>), context);
        configurator.UseConsumeFilter(typeof(CorrelationConsumeFilter<>), context);
    }
}
