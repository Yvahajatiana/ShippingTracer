using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Infrastructure.Persistence;

namespace ParcelFlow.Reporting.Infrastructure;

public sealed class ReportingTransactionBehavior<TRequest, TResponse>(ReportingDbContext database)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IReportingCommand || database.Database.CurrentTransaction is not null)
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
            throw new ReportingConflictException("Reporting state changed concurrently. Retry the operation.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ReportingConflictException("The reporting event identity already exists.");
        }
    }
}

