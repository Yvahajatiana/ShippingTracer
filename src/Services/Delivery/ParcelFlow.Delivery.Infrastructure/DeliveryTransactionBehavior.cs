using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Infrastructure.Persistence;

namespace ParcelFlow.Delivery.Infrastructure;

public sealed class DeliveryTransactionBehavior<TRequest, TResponse>(DeliveryDbContext database)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IDeliveryCommand || database.Database.CurrentTransaction is not null)
            return await next();
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var result = await next();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DeliveryConflictException("Delivery state changed concurrently. Retry the operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DeliveryConflictException("The shipment identity, route stop or operation identifier already exists.");
        }
    }
}
