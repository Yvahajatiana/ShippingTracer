using Asp.Versioning;
using MediatR;
using ParcelFlow.Shipment.Application;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Api;

public static class ShipmentEndpoints
{
    public static void MapShipmentEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var api = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(versions).MapToApiVersion(1, 0)
            .RequireAuthorization("customer-or-staff");
        var shipments = api.MapGroup("/shipments").WithTags("Shipments");
        shipments.MapPost("/quote", async (ShipmentBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new QuoteShipment(body.Sender, body.Recipient, body.Parcel), ct)));
        shipments.MapPost("", async (ShipmentBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateShipment(body.Sender, body.Recipient, body.Parcel), ct);
            return TypedResults.Created($"/api/v1/shipments/{result.Id}", result);
        });
        shipments.MapGet("", async (ISender sender, CancellationToken ct, int offset = 0, int limit = 20) =>
            TypedResults.Ok(await sender.Send(new ListShipments(offset, limit), ct)));
        shipments.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetShipment(id), ct)));
        shipments.MapGet("/{id:guid}/label", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var shipment = await sender.Send(new GetShipment(id), ct);
            var pdf = await sender.Send(new GetLabel(id), ct);
            return TypedResults.File(pdf, "application/pdf", $"{shipment.TrackingNumber}.pdf");
        });
        var tariffs = api.MapGroup("/tariffs").WithTags("Tariffs");
        tariffs.MapGet("", async (ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new ListTariffs(), ct)));
        tariffs.MapPut("/{zone}", async (DeliveryZone zone, TariffBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new UpdateTariff(zone, body.BasePrice, body.PricePerKg), ct)))
            .RequireAuthorization("admin");
    }
}

public sealed record ShipmentBody(AddressInput Sender, AddressInput Recipient, ParcelInput Parcel);
public sealed record TariffBody(decimal BasePrice, decimal PricePerKg);
