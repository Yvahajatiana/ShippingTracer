using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Contracts.Billing.V1;
using ParcelFlow.Billing.Application;

namespace ParcelFlow.Billing.Infrastructure.Consumers;

public sealed class PaymentRequestedConsumer(ISender sender) : IConsumer<PaymentRequested>
{
    public async Task Consume(ConsumeContext<PaymentRequested> context) =>
        await sender.Send(new RegisterInvoice(context.Message.ShipmentId, context.Message.CustomerSubject,
            context.Message.Amount, context.Message.Currency, context.Message.OccurredAtUtc), context.CancellationToken);
}
