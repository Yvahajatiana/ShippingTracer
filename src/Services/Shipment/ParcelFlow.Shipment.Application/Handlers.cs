using MassTransit;
using MediatR;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Shipment.V1;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Application;

internal sealed class ShipmentAccess(IShipmentStore store, ICurrentUser user)
{
    public bool IsStaff => user.IsInRole("admin") || user.IsInRole("operator");
    public void RequireUser()
    {
        if (string.IsNullOrWhiteSpace(user.Subject) || !(IsStaff || user.IsInRole("customer"))) throw new ShipmentForbiddenException();
    }
    public async Task<ParcelShipment> Find(Guid id, CancellationToken ct)
    {
        RequireUser();
        var shipment = await store.FindShipmentAsync(id, ct) ?? throw new ShipmentNotFoundException("Shipment not found.");
        if (!IsStaff && shipment.OwnerSubject != user.Subject) throw new ShipmentForbiddenException();
        return shipment;
    }
}

internal sealed class ShipmentHandlers(IShipmentStore store, ICurrentUser user, ITrackingNumberGenerator numbers,
    IShippingLabelGenerator labels, IPublishEndpoint publisher, ShipmentAccess access) :
    IRequestHandler<CreateShipment, ShipmentDto>, IRequestHandler<QuoteShipment, QuoteDto>, IRequestHandler<GetShipment, ShipmentDto>,
    IRequestHandler<ListShipments, IReadOnlyList<ShipmentDto>>, IRequestHandler<GetLabel, byte[]>,
    IRequestHandler<ListTariffs, IReadOnlyList<TariffDto>>, IRequestHandler<UpdateTariff, TariffDto>
{
    public async Task<ShipmentDto> Handle(CreateShipment request, CancellationToken ct)
    {
        access.RequireUser();
        var sender = request.Sender.ToDomain();
        var recipient = request.Recipient.ToDomain();
        var parcel = request.Parcel.ToDomain();
        var zone = ShippingZones.Resolve(sender.CountryCode, recipient.CountryCode);
        var tariff = await FindTariff(zone, ct);
        var shipment = new ParcelShipment(user.Subject, numbers.Generate(), sender, recipient, parcel, tariff.CalculatePrice(parcel), zone);
        labels.Generate(shipment);
        store.AddShipment(shipment);
        await publisher.Publish(new ShipmentCreated(DeterministicGuid.Create("shipment:created:", shipment.Id), shipment.Id, DateTimeOffset.UtcNow, shipment.Id,
            shipment.TrackingNumber, shipment.OwnerSubject, shipment.Price, shipment.Currency), ct);
        await store.SaveChangesAsync(ct);
        return shipment.ToDto();
    }
    public async Task<QuoteDto> Handle(QuoteShipment request, CancellationToken ct)
    {
        access.RequireUser();
        var sender = request.Sender.ToDomain();
        var recipient = request.Recipient.ToDomain();
        var parcel = request.Parcel.ToDomain();
        var zone = ShippingZones.Resolve(sender.CountryCode, recipient.CountryCode);
        var tariff = await FindTariff(zone, ct);
        return new(tariff.CalculatePrice(parcel), tariff.Currency, zone, parcel.BillableWeightKg);
    }
    public async Task<ShipmentDto> Handle(GetShipment request, CancellationToken ct) => (await access.Find(request.ShipmentId, ct)).ToDto();
    public async Task<byte[]> Handle(GetLabel request, CancellationToken ct) => labels.Generate(await access.Find(request.ShipmentId, ct));
    public async Task<IReadOnlyList<ShipmentDto>> Handle(ListShipments request, CancellationToken ct)
    {
        access.RequireUser();
        return (await store.ListShipmentsAsync(access.IsStaff ? null : user.Subject, request.Offset, request.Limit, ct)).Select(x => x.ToDto()).ToArray();
    }
    public async Task<IReadOnlyList<TariffDto>> Handle(ListTariffs request, CancellationToken ct)
    {
        access.RequireUser();
        return (await store.ListTariffsAsync(ct)).Select(x => x.ToDto()).ToArray();
    }
    public async Task<TariffDto> Handle(UpdateTariff request, CancellationToken ct)
    {
        if (!user.IsInRole("admin")) throw new ShipmentForbiddenException();
        var tariff = await FindTariff(request.Zone, ct);
        tariff.Update(request.BasePrice, request.PricePerKg);
        await store.SaveChangesAsync(ct);
        return tariff.ToDto();
    }
    private async Task<Tariff> FindTariff(DeliveryZone zone, CancellationToken ct) =>
        await store.FindTariffAsync(zone, ct) ?? throw new ShipmentConflictException("No tariff is configured for this zone.");
}
