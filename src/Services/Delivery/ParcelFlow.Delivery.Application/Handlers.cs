using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Tracking.V1;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Application;

internal static class HandlerRules
{
    public static async Task<DeliveryRoute> Route(IDeliveryStore store, Guid id, CancellationToken ct) { await store.LockRouteAsync(id, ct); return await store.FindRouteAsync(id, ct) ?? throw new DeliveryNotFoundException("Route not found."); }
    public static void Authorize(DeliveryRoute route, IDeliveryActor actor) { if (!actor.IsAdmin && route.DriverSubject != actor.Subject) throw new DeliveryForbiddenException("Only the assigned driver or an administrator can perform this operation."); }
    public static void Apply(Action action) { try { action(); } catch (InvalidOperationException ex) { throw new DeliveryConflictException(ex.Message); } }
    public static async Task<DeliveryParcel> Parcel(IDeliveryStore store, Guid id, CancellationToken ct) { await store.LockParcelAsync(id, ct); return await store.FindParcelAsync(id, ct) ?? throw new DeliveryNotFoundException("Parcel not found."); }
}
public sealed class CreateDriverHandler(IDeliveryStore store) : IRequestHandler<CreateDriver, DriverDto>
{
    public async Task<DriverDto> Handle(CreateDriver request, CancellationToken ct)
    {
        var subject = request.Subject.Trim(); await store.LockDriverAsync(subject, ct);
        if (await store.FindDriverAsync(subject, ct) is not null) throw new DeliveryConflictException("Driver already exists.");
        var driver = new DeliveryDriver(subject, request.DisplayName); store.AddDriver(driver); await store.SaveChangesAsync(ct); return driver.ToDto();
    }
}
public sealed class UpdateDriverHandler(IDeliveryStore store) : IRequestHandler<UpdateDriver, DriverDto>
{
    public async Task<DriverDto> Handle(UpdateDriver request, CancellationToken ct)
    {
        var subject = request.Subject.Trim(); await store.LockDriverAsync(subject, ct);
        var driver = await store.FindDriverAsync(subject, ct) ?? throw new DeliveryNotFoundException("Driver not found.");
        if (!request.IsActive && await store.HasOpenRoutesAsync(subject, ct)) throw new DeliveryConflictException("Driver has open routes.");
        driver.Update(request.DisplayName, request.IsActive); await store.SaveChangesAsync(ct); return driver.ToDto();
    }
}
public sealed class CreateRouteHandler(IDeliveryStore store) : IRequestHandler<CreateRoute, RouteDto>
{
    public async Task<RouteDto> Handle(CreateRoute request, CancellationToken ct)
    {
        await store.LockRouteAsync(request.Id, ct); if (await store.FindRouteAsync(request.Id, ct) is not null) throw new DeliveryConflictException("Route already exists.");
        var route = new DeliveryRoute(request.Id, request.Name, request.ServiceDate); store.AddRoute(route); await store.SaveChangesAsync(ct); return route.ToDto();
    }
}
public sealed class AssignRouteDriverHandler(IDeliveryStore store) : IRequestHandler<AssignRouteDriver, RouteDto>
{
    public async Task<RouteDto> Handle(AssignRouteDriver request, CancellationToken ct)
    {
        var route = await HandlerRules.Route(store, request.RouteId, ct); var subject = request.DriverSubject.Trim(); await store.LockDriverAsync(subject, ct);
        var driver = await store.FindDriverAsync(subject, ct) ?? throw new DeliveryNotFoundException("Driver not found.");
        HandlerRules.Apply(() => route.AssignDriver(driver)); await store.SaveChangesAsync(ct); return route.ToDto();
    }
}
public sealed class AddRouteParcelHandler(IDeliveryStore store) : IRequestHandler<AddRouteParcel, RouteDto>
{
    public async Task<RouteDto> Handle(AddRouteParcel request, CancellationToken ct)
    {
        var route = await HandlerRules.Route(store, request.RouteId, ct); var parcel = await HandlerRules.Parcel(store, request.ShipmentId, ct);
        HandlerRules.Apply(() => route.AddParcel(parcel)); await store.SaveChangesAsync(ct); return route.ToDto();
    }
}
public sealed class RemoveRouteParcelHandler(IDeliveryStore store) : IRequestHandler<RemoveRouteParcel, RouteDto>
{
    public async Task<RouteDto> Handle(RemoveRouteParcel request, CancellationToken ct)
    {
        var route = await HandlerRules.Route(store, request.RouteId, ct); var parcel = await HandlerRules.Parcel(store, request.ShipmentId, ct);
        HandlerRules.Apply(() => route.RemoveParcel(parcel)); await store.SaveChangesAsync(ct); return route.ToDto();
    }
}
public sealed class RegisterReadyShipmentHandler(IDeliveryStore store) : IRequestHandler<RegisterReadyShipment, bool>
{
    public async Task<bool> Handle(RegisterReadyShipment request, CancellationToken ct)
    {
        await store.LockParcelAsync(request.ShipmentId, ct); var parcel = await store.FindParcelAsync(request.ShipmentId, ct); var isNew = parcel is null; parcel ??= new(request.ShipmentId);
        bool changed = false; HandlerRules.Apply(() => changed = parcel.Register(request.TrackingNumber));
        if (isNew) store.AddParcel(parcel); if (changed) await store.SaveChangesAsync(ct); return changed;
    }
}
public sealed class RegisterParcelPickupHandler(IDeliveryStore store) : IRequestHandler<RegisterParcelPickup, bool>
{
    public async Task<bool> Handle(RegisterParcelPickup request, CancellationToken ct)
    {
        await store.LockParcelAsync(request.ShipmentId, ct); var parcel = await store.FindParcelAsync(request.ShipmentId, ct); var isNew = parcel is null; parcel ??= new(request.ShipmentId);
        var changed = parcel.MarkPickedUp(); if (isNew) store.AddParcel(parcel); if (changed) await store.SaveChangesAsync(ct); return changed;
    }
}
public sealed class StartRouteHandler(IDeliveryStore store, IDeliveryActor actor, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<StartRoute, RouteDto>
{
    public async Task<RouteDto> Handle(StartRoute request, CancellationToken ct)
    {
        var route = await HandlerRules.Route(store, request.RouteId, ct); HandlerRules.Authorize(route, actor);
        if (route.DriverSubject is null) throw new DeliveryConflictException("Route has no driver.");
        await store.LockDriverAsync(route.DriverSubject, ct); var driver = await store.FindDriverAsync(route.DriverSubject, ct) ?? throw new DeliveryConflictException("Driver not found.");
        var parcels = new List<DeliveryParcel>();
        foreach (var stop in route.Stops.OrderBy(x => x.ShipmentId))
        {
            var parcel = await HandlerRules.Parcel(store, stop.ShipmentId, ct);
            if (parcel.ActiveRouteId != route.Id || !parcel.IsPickedUp || parcel.IsDelivered || parcel.TrackingNumber is null) throw new DeliveryConflictException("Parcel is not available for this route.");
            parcels.Add(parcel);
        }
        var now = DeliveryRules.NormalizeTimestamp(clock.GetUtcNow()); HandlerRules.Apply(() => route.Start(driver, now));
        foreach (var parcel in parcels)
        {
            var at = parcel.RecordEvent(now);
            await publisher.Publish(new ParcelStatusRecorded(DeterministicGuid.Create("delivery:out-for-delivery:", route.Id, parcel.ShipmentId), parcel.ShipmentId, at, parcel.ShipmentId, parcel.TrackingNumber!, TrackingStatus.OutForDelivery, null), ct);
        }
        await store.SaveChangesAsync(ct); return route.ToDto();
    }
}
public sealed class ConfirmDeliveryHandler(IDeliveryStore store, IDeliveryActor actor, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<ConfirmDelivery, AttemptDto>
{
    public Task<AttemptDto> Handle(ConfirmDelivery request, CancellationToken ct) => DeliveryCompletion.Complete(store, actor, publisher, clock, request.OperationId, request.RouteId, request.ShipmentId, DeliveryOutcome.Delivered, request.RecipientName.Trim(), null, request.SignaturePng, request.Photo, ct);
}
public sealed class FailDeliveryHandler(IDeliveryStore store, IDeliveryActor actor, IPublishEndpoint publisher, TimeProvider clock) : IRequestHandler<FailDelivery, AttemptDto>
{
    public Task<AttemptDto> Handle(FailDelivery request, CancellationToken ct) => DeliveryCompletion.Complete(store, actor, publisher, clock, request.OperationId, request.RouteId, request.ShipmentId, DeliveryOutcome.Failed, null, request.Reason, null, null, ct);
}
internal static class DeliveryCompletion
{
    public static async Task<AttemptDto> Complete(IDeliveryStore store, IDeliveryActor actor, IPublishEndpoint publisher, TimeProvider clock, Guid operationId, Guid routeId, Guid shipmentId, DeliveryOutcome outcome, string? recipient, DeliveryFailureReason? reason, byte[]? signature, byte[]? photo, CancellationToken ct)
    {
        var route = await HandlerRules.Route(store, routeId, ct); HandlerRules.Authorize(route, actor);
        var parcel = await HandlerRules.Parcel(store, shipmentId, ct);
        var existing = await store.FindAttemptAsync(operationId, ct);
        if (existing is not null)
        {
            if (!existing.Matches(routeId, shipmentId, outcome, actor.Subject, recipient, reason, signature, photo)) throw new DeliveryConflictException("Operation ID identifies another delivery attempt.");
            return existing.ToDto();
        }
        if (parcel.ActiveRouteId != routeId || parcel.IsDelivered) throw new DeliveryConflictException("Parcel is not assigned to this route.");
        var at = parcel.RecordEvent(clock.GetUtcNow());
        HandlerRules.Apply(() => route.CompleteStop(shipmentId, outcome, reason, at));
        parcel.Complete(routeId, outcome == DeliveryOutcome.Delivered);
        var attempt = new DeliveryAttempt(operationId, routeId, shipmentId, outcome, at, actor.Subject, recipient, reason, signature, photo, photo is null ? null : ProofImages.PhotoContentType(photo));
        store.AddAttempt(attempt);
        await publisher.Publish(new ParcelStatusRecorded(operationId, shipmentId, at, shipmentId, parcel.TrackingNumber!, outcome == DeliveryOutcome.Delivered ? TrackingStatus.Delivered : TrackingStatus.Failed, null), ct);
        await store.SaveChangesAsync(ct); return attempt.ToDto();
    }
}
public sealed class GetRouteHandler(IDeliveryStore store) : IRequestHandler<GetRoute, RouteDto>
{
    public async Task<RouteDto> Handle(GetRoute request, CancellationToken ct) => (await store.FindRouteAsync(request.RouteId, ct) ?? throw new DeliveryNotFoundException("Route not found.")).ToDto();
}
public sealed class ListRoutesHandler(IDeliveryStore store) : IRequestHandler<ListRoutes, RoutePageDto> { public Task<RoutePageDto> Handle(ListRoutes request, CancellationToken ct) => store.ListRoutesAsync(request.Page, request.PageSize, ct); }
public sealed class ListDriversHandler(IDeliveryStore store) : IRequestHandler<ListDrivers, DriverPageDto> { public Task<DriverPageDto> Handle(ListDrivers request, CancellationToken ct) => store.ListDriversAsync(request.Page, request.PageSize, ct); }
public sealed class ListAvailableParcelsHandler(IDeliveryStore store) : IRequestHandler<ListAvailableParcels, AvailableParcelPageDto> { public Task<AvailableParcelPageDto> Handle(ListAvailableParcels request, CancellationToken ct) => store.ListAvailableParcelsAsync(request.Page, request.PageSize, ct); }
public sealed class GetDeliveryProofHandler(IDeliveryStore store, IDeliveryActor actor) : IRequestHandler<GetDeliveryProof, AttemptDto>
{
    public async Task<AttemptDto> Handle(GetDeliveryProof request, CancellationToken ct)
    {
        var route = await store.FindRouteAsync(request.RouteId, ct) ?? throw new DeliveryNotFoundException("Route not found."); HandlerRules.Authorize(route, actor);
        return (await store.FindAttemptByStopAsync(request.RouteId, request.ShipmentId, ct) ?? throw new DeliveryNotFoundException("Delivery proof not found.")).ToDto();
    }
}
public sealed class GetDeliveryProofImageHandler(IDeliveryStore store, IDeliveryActor actor) : IRequestHandler<GetDeliveryProofImage, ProofImageDto>
{
    public async Task<ProofImageDto> Handle(GetDeliveryProofImage request, CancellationToken ct)
    {
        var route = await store.FindRouteAsync(request.RouteId, ct) ?? throw new DeliveryNotFoundException("Route not found."); HandlerRules.Authorize(route, actor);
        var attempt = await store.FindAttemptByStopAsync(request.RouteId, request.ShipmentId, ct) ?? throw new DeliveryNotFoundException("Delivery proof not found.");
        return request.Kind switch
        {
            ProofImageKind.Signature when attempt.SignaturePng is not null => new(attempt.SignaturePng.ToArray(), "image/png"),
            ProofImageKind.Photo when attempt.Photo is not null => new(attempt.Photo.ToArray(), attempt.PhotoContentType!),
            _ => throw new DeliveryNotFoundException("Proof image not found.")
        };
    }
}
