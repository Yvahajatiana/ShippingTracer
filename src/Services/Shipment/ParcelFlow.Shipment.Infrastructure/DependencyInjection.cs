using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ParcelFlow.Shipment.Infrastructure.Booking;
using ParcelFlow.Shipment.Infrastructure.Labels;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Infrastructure.Persistence;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ParcelFlow.Shipment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddShipmentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ShipmentDbContext>((provider, options) => options.UseNpgsql(
            provider.GetRequiredService<IConfiguration>().GetConnectionString("ShipmentDatabase")
                ?? throw new InvalidOperationException("ConnectionStrings:ShipmentDatabase is required.")));
        services.AddScoped<IShipmentStore, ShipmentStore>();
        services.AddSingleton<ITrackingNumberGenerator, TrackingNumberGenerator>();
        services.AddSingleton<IShippingLabelGenerator, ShippingLabelGenerator>();
        services.AddScoped<PaymentCompletedActivity>();
        services.AddScoped<PaymentFailedActivity>();
        services.AddScoped<ParcelPickedUpActivity>();
        services.AddScoped<PaymentTimeoutActivity>();
        services.AddOptions<ShipmentBookingOptions>()
            .Configure<IConfiguration>((options, settings) =>
            {
                var timeout = settings[$"{ShipmentBookingOptions.SectionName}:PaymentTimeout"];
                if (!string.IsNullOrWhiteSpace(timeout))
                    options.PaymentTimeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);
            })
            .Validate(options => options.PaymentTimeout > TimeSpan.Zero, "ShipmentBooking:PaymentTimeout must be positive.")
            .Validate(options => options.PaymentTimeout <= ShipmentBookingOptions.MaximumPaymentTimeout,
                $"ShipmentBooking:PaymentTimeout must not exceed {ShipmentBookingOptions.MaximumPaymentTimeout} " +
                "(RabbitMQ delayed message exchange limit of 2^32-1 ms).")
            .ValidateOnStart();
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

        services.AddMassTransit(bus =>
        {
            // Payment timeouts use RabbitMQ delayed delivery (rabbitmq_delayed_message_exchange plugin).
            bus.AddDelayedMessageScheduler();
            bus.AddEntityFrameworkOutbox<ShipmentDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.QueryDelay = TimeSpan.FromSeconds(1);
            });
            bus.AddSagaStateMachine<ShipmentBookingStateMachine, ShipmentBookingState>()
                .EntityFrameworkRepository(repository =>
                {
                    repository.ConcurrencyMode = ConcurrencyMode.Optimistic;
                    repository.ExistingDbContext<ShipmentDbContext>();
                    repository.UsePostgres();
                });
            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                endpoint.UseMessageRetry(retry => retry.Interval(3, TimeSpan.FromMilliseconds(200)));
                endpoint.UseEntityFrameworkOutbox<ShipmentDbContext>(context);
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
                rabbit.UseDelayedMessageScheduler();
                rabbit.ConfigureEndpoints(context);
            });
        });
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        return services;
    }
}
