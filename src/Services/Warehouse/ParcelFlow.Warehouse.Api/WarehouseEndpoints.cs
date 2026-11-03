using System.Security.Claims;
using System.Text.Json.Serialization;
using Asp.Versioning;
using MediatR;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;

namespace ParcelFlow.Warehouse.Api;

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var warehouses = app.MapGroup("/api/v{version:apiVersion}/warehouses")
            .WithApiVersionSet(versions).MapToApiVersion(1, 0).WithTags("Warehouses")
            .RequireAuthorization("operations");
        warehouses.MapPost("", async (CreateWarehouseBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateWarehouse(Guid.NewGuid(), body.Code, body.Name,
                body.Kind, body.Street, body.City, body.PostalCode, body.CountryCode), ct);
            return TypedResults.Created($"/api/v1/warehouses/{result.Id}", result);
        }).RequireAuthorization("admin");
        warehouses.MapPut("/{warehouseId:guid}", async (Guid warehouseId, UpdateWarehouseBody body,
            ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new UpdateWarehouse(
                warehouseId, body.Name, body.Street, body.City, body.PostalCode, body.CountryCode, body.IsActive), ct)))
            .RequireAuthorization("admin");
        warehouses.MapGet("", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListWarehouses(page, pageSize), ct)));
        warehouses.MapGet("/{warehouseId:guid}", async (Guid warehouseId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetWarehouse(warehouseId), ct)));
        warehouses.MapGet("/{warehouseId:guid}/stock", async (Guid warehouseId, ISender sender,
            CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new GetWarehouseStock(warehouseId, page, pageSize), ct)));
        warehouses.MapPost("/scans", async (ScanParcelBody body, ClaimsPrincipal user, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new ScanParcel(body.ScanId, body.WarehouseId,
                body.TrackingNumber, body.Direction, user.FindFirst("sub")!.Value), ct)));
    }
}

public sealed record CreateWarehouseBody(string Code, string Name, [property: JsonRequired] WarehouseKind Kind,
    string Street, string City, string PostalCode, string CountryCode);
public sealed record UpdateWarehouseBody(string Name, string Street, string City,
    string PostalCode, string CountryCode, [property: JsonRequired] bool IsActive);
public sealed record ScanParcelBody(Guid ScanId, Guid WarehouseId, string TrackingNumber, [property: JsonRequired] ScanDirection Direction);
