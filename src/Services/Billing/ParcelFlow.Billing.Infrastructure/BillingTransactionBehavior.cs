using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Infrastructure.Persistence;

namespace ParcelFlow.Billing.Infrastructure;

public sealed class BillingTransactionBehavior<TRequest, TResponse>(BillingDbContext database)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IBillingCommand || database.Database.CurrentTransaction is not null)
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
            throw new BillingConflictException("Billing state changed concurrently. Retry the operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BillingConflictException("The invoice identity or operation identifier already exists.");
        }
    }
}
