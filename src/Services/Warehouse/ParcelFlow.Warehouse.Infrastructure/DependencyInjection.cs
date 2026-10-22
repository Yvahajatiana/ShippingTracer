using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ParcelFlow.Warehouse.Infrastructure.Consumers;

using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Infrastructure.Persistence;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ParcelFlow.Warehouse.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddWarehouseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<WarehouseDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("WarehouseDatabase")
                ?? throw new InvalidOperationException("ConnectionStrings:WarehouseDatabase is required.")));
        services.AddScoped<IWarehouseStore, WarehouseStore>();
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(WarehouseTransactionBehavior<,>));
        services.AddStackExchangeRedisCache(options => options.InstanceName = "parcelflow:");
        services.AddOptions<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCacheOptions>()
            .Configure<IConfiguration>((options, settings) =>
            {
                options.Configuration = settings.GetConnectionString("Redis")
                    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
            });
        services.AddSingleton(new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(exception => exception.IsTransient)
                    .Handle<TimeoutException>(),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<NpgsqlException>(exception => exception.IsTransient)
                    .Handle<TimeoutException>(),
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15)
            }).Build());

        services.Configure<MassTransitHostOptions>(options => { options.WaitUntilStarted = true; options.StartTimeout = TimeSpan.FromSeconds(30); });
        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter("warehouse", false));
            bus.AddEntityFrameworkOutbox<WarehouseDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
                outbox.IsolationLevel = System.Data.IsolationLevel.ReadCommitted;
            });
            bus.AddConsumer<ShipmentReadyForPickupConsumer>();
            bus.AddConsumer<ParcelDeliveryStartedConsumer>();


            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromMilliseconds(200)));
                endpoint.UseEntityFrameworkOutbox<WarehouseDbContext>(context);
            });
            bus.UsingRabbitMq((context, rabbit) =>
            {
                var settings = context.GetRequiredService<IConfiguration>();
                rabbit.Host(settings["RabbitMq:Host"] ?? "localhost",
                    ushort.Parse(settings["RabbitMq:Port"] ?? "5672", System.Globalization.CultureInfo.InvariantCulture),
                    settings["RabbitMq:VirtualHost"] ?? "/", host =>
                    {
                        host.Username(settings["RabbitMq:Username"]
                            ?? throw new InvalidOperationException("RabbitMq:Username is required."));
                        host.Password(settings["RabbitMq:Password"]
                            ?? throw new InvalidOperationException("RabbitMq:Password is required."));
                    });
                rabbit.ConfigureEndpoints(context);
            });
        });
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        return services;
    }
}
