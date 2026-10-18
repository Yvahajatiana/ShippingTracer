using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Infrastructure.Persistence;

namespace ParcelFlow.Notification.Infrastructure;

public sealed class NotificationTransactionBehavior<TRequest, TResponse>(NotificationDbContext database)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not INotificationCommand || database.Database.CurrentTransaction is not null)
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
            throw new NotificationConflictException("Notification state changed concurrently. Retry the operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new NotificationConflictException("The notification event identity already exists.");
        }
    }
}
