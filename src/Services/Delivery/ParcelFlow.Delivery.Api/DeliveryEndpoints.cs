using System.Security.Claims;
using System.Text.Json.Serialization;
using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using ParcelFlow.Delivery.Application;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Api;

public sealed class HttpDeliveryActor(IHttpContextAccessor accessor) : IDeliveryActor
{
    public string Subject => accessor.HttpContext?.User.FindFirst("sub")?.Value ?? string.Empty;
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("admin") == true;
}

public static class DeliveryEndpoints
{
    public static void MapDeliveryEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var delivery = app.MapGroup("/api/v{version:apiVersion}/delivery")
            .WithApiVersionSet(versions).MapToApiVersion(1, 0).WithTags("Delivery")
            .RequireAuthorization("operations");
        delivery.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        delivery.MapPost("/drivers", async (CreateDriverBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateDriver(body.Subject, body.DisplayName), ct);
            return TypedResults.Created($"/api/v1/delivery/drivers/{Uri.EscapeDataString(result.Subject)}", result);
        }).RequireAuthorization("admin");
        delivery.MapPut("/drivers/{subject}", async (string subject, UpdateDriverBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new UpdateDriver(subject, body.DisplayName, body.IsActive), ct)))
            .RequireAuthorization("admin");
        delivery.MapGet("/drivers", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListDrivers(page, pageSize), ct)));
        delivery.MapPost("/routes", async (CreateRouteBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateRoute(Guid.NewGuid(), body.Name, body.ServiceDate), ct);
            return TypedResults.Created($"/api/v1/delivery/routes/{result.Id}", result);
        });
        delivery.MapGet("/routes", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListRoutes(page, pageSize), ct)));
        delivery.MapGet("/routes/{routeId:guid}", async (Guid routeId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetRoute(routeId), ct)));
        delivery.MapPut("/routes/{routeId:guid}/driver", async (Guid routeId, AssignDriverBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new AssignRouteDriver(routeId, body.DriverSubject), ct)));
        delivery.MapPost("/routes/{routeId:guid}/stops", async (Guid routeId, AddStopBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new AddRouteParcel(routeId, body.ShipmentId), ct)));
        delivery.MapDelete("/routes/{routeId:guid}/stops/{shipmentId:guid}", async (Guid routeId, Guid shipmentId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveRouteParcel(routeId, shipmentId), ct);
            return TypedResults.NoContent();
        });
        delivery.MapPost("/routes/{routeId:guid}/start", async (Guid routeId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new StartRoute(routeId), ct)));
        delivery.MapPost("/routes/{routeId:guid}/stops/{shipmentId:guid}/delivery", async (Guid routeId, Guid shipmentId,
            ConfirmDeliveryBody body, ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new ConfirmDelivery(
                body.OperationId, routeId, shipmentId, body.RecipientName, body.SignaturePng, body.Photo), ct)))
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024));
        delivery.MapPost("/routes/{routeId:guid}/stops/{shipmentId:guid}/failure", async (Guid routeId, Guid shipmentId,
            FailDeliveryBody body, ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new FailDelivery(
                body.OperationId, routeId, shipmentId, body.Reason), ct)));
        delivery.MapGet("/routes/{routeId:guid}/stops/{shipmentId:guid}/proof", async (Guid routeId, Guid shipmentId,
            ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new GetDeliveryProof(routeId, shipmentId), ct)));
        delivery.MapGet("/routes/{routeId:guid}/stops/{shipmentId:guid}/proof/{kind}", async (Guid routeId, Guid shipmentId,
            string kind, HttpContext context, ISender sender, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ProofImageKind>(kind, true, out var imageKind) || !Enum.IsDefined(imageKind) ||
                !string.Equals(kind, imageKind.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new BadHttpRequestException("Unknown proof image kind.");
            var image = await sender.Send(new GetDeliveryProofImage(routeId, shipmentId, imageKind), ct);
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var extension = image.ContentType == "image/jpeg" ? "jpg" : "png";
            return TypedResults.File(image.Content, image.ContentType, $"{imageKind.ToString().ToLowerInvariant()}.{extension}");
        });
        delivery.MapGet("/parcels/available", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListAvailableParcels(page, pageSize), ct)));
    }
}

public sealed record CreateDriverBody(string Subject, string DisplayName);
public sealed record UpdateDriverBody(string DisplayName, [property: JsonRequired] bool IsActive);
public sealed record CreateRouteBody(string Name, [property: JsonRequired] DateOnly ServiceDate);
public sealed record AssignDriverBody(string DriverSubject);
public sealed record AddStopBody([property: JsonRequired] Guid ShipmentId);
public sealed record ConfirmDeliveryBody([property: JsonRequired] Guid OperationId, string RecipientName, byte[]? SignaturePng, byte[]? Photo);
public sealed record FailDeliveryBody([property: JsonRequired] Guid OperationId, [property: JsonRequired] DeliveryFailureReason Reason);
