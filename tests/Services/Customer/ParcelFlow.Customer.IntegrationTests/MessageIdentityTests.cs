using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Customer.V1;
using ParcelFlow.Customer.Application;
using ParcelFlow.Customer.Infrastructure.Persistence;
using Xunit;

namespace ParcelFlow.Customer.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class MessageIdentityTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Request_correlation_and_event_identity_reach_the_broker_through_the_outbox()
    {
        var received = new ConcurrentQueue<ConsumeContext<CustomerProfileCreated>>();
        var listener = Bus.Factory.CreateUsingRabbitMq(configurator =>
        {
            configurator.Host(new Uri(infrastructure.RabbitMq.GetConnectionString()));
            configurator.ReceiveEndpoint($"customer-identity-tests-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.AutoDelete = true;
                endpoint.Durable = false;
                endpoint.Handler<CustomerProfileCreated>(context =>
                {
                    received.Enqueue(context);
                    return Task.CompletedTask;
                });
            });
        });
        await listener.StartAsync();
        try
        {
            await using var factory = new CustomerApiFactory(infrastructure);
            await using (var scope = factory.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Database.MigrateAsync();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Add("X-Test-Role", "customer");
            var correlationId = Guid.NewGuid();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/customers")
            {
                Content = JsonContent.Create(new CreateProfile("Marie", "Martin", "marie@example.test", null))
            };
            request.Headers.Add("X-Correlation-Id", correlationId.ToString());

            using var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            response.Headers.GetValues("X-Correlation-Id").Should().Equal(correlationId.ToString("D"));
            var profile = (await response.Content.ReadFromJsonAsync<ProfileDto>())!;
            var delivered = await WaitFor(received, message => message.Message.CustomerId == profile.Id);
            delivered.Message.EventId.Should().Be(DeterministicGuid.Create("customer:profile-created:", profile.Id));
            delivered.MessageId.Should().Be(delivered.Message.EventId);
            delivered.Message.CorrelationId.Should().Be(correlationId);
            delivered.CorrelationId.Should().Be(correlationId);
        }
        finally
        {
            await listener.StopAsync();
        }
    }

    private static async Task<ConsumeContext<T>> WaitFor<T>(ConcurrentQueue<ConsumeContext<T>> received,
        Func<ConsumeContext<T>, bool> predicate) where T : class
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (received.FirstOrDefault(predicate) is { } match) return match;
            await Task.Delay(100, timeout.Token);
        }
    }
}
