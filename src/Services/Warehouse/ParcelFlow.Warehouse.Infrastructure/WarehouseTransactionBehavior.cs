using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Infrastructure.Persistence;

namespace ParcelFlow.Warehouse.Infrastructure;

public sealed class WarehouseTransactionBehavior<TRequest, TResponse>(WarehouseDbContext database)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IWarehouseCommand || database.Database.CurrentTransaction is not null)
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
            throw new WarehouseConflictException("Warehouse state changed concurrently. Retry the operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new WarehouseConflictException("The warehouse code, shipment identity or scan identifier already exists.");
        }
    }
}
