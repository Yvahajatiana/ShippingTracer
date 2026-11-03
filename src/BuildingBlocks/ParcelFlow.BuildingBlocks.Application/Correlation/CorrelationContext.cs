using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ParcelFlow.BuildingBlocks.Application.Correlation;

/// <summary>Correlation of the operation in progress: the HTTP request or the consumed message.</summary>
public interface ICorrelationContext
{
    /// <summary>
    /// Identifier to stamp on the integration events published by the current operation. It is the incoming
    /// correlation when one was received, otherwise a value generated once for the whole operation.
    /// </summary>
    Guid CorrelationId { get; }
}

/// <summary>Scoped holder of the ambient correlation, filled by the HTTP middleware or the consume filter.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private Guid? _correlationId;

    public Guid CorrelationId => _correlationId ??= Guid.NewGuid();

    public void Set(Guid correlationId)
    {
        if (correlationId == Guid.Empty) throw new ArgumentException("Correlation ID is required.", nameof(correlationId));
        _correlationId = correlationId;
    }

    /// <summary>Creates a context already carrying <paramref name="correlationId"/>.</summary>
    public static CorrelationContext From(Guid correlationId)
    {
        var context = new CorrelationContext();
        context.Set(correlationId);
        return context;
    }
}

public static class CorrelationServiceCollectionExtensions
{
    /// <summary>Registers the scoped ambient correlation; safe to call from several layers.</summary>
    public static IServiceCollection AddParcelFlowCorrelation(this IServiceCollection services)
    {
        services.TryAddScoped<CorrelationContext>();
        services.TryAddScoped<ICorrelationContext>(provider => provider.GetRequiredService<CorrelationContext>());
        return services;
    }
}
