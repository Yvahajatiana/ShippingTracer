using MediatR;
using ParcelFlow.Shipment.Domain;

namespace ParcelFlow.Shipment.Application;

public interface ICurrentUser
{
    string Subject { get; }
    bool IsInRole(string role);
}
public interface IShipmentStore
{
    Task<ParcelShipment?> FindShipmentAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ParcelShipment>> ListShipmentsAsync(string? ownerSubject, int offset, int limit, CancellationToken cancellationToken);
    Task<Tariff?> FindTariffAsync(DeliveryZone zone, CancellationToken cancellationToken);
    Task<IReadOnlyList<Tariff>> ListTariffsAsync(CancellationToken cancellationToken);
    void AddShipment(ParcelShipment shipment);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
public interface ITrackingNumberGenerator { string Generate(); }
public interface IShippingLabelGenerator { byte[] Generate(ParcelShipment shipment); }
public sealed class ShipmentNotFoundException(string message) : Exception(message);
public sealed class ShipmentConflictException(string message) : Exception(message);
public sealed class ShipmentForbiddenException() : Exception("You do not have access to this resource.");

public sealed record AddressInput(string Name, string Email, string Phone, string Line1, string? Line2, string PostalCode, string City, string CountryCode);
public sealed record ParcelInput(decimal WeightKg, decimal LengthCm, decimal WidthCm, decimal HeightCm);
public sealed record ShipmentDto(Guid Id, string TrackingNumber, string OwnerSubject, AddressInput Sender, AddressInput Recipient,
    ParcelInput Parcel, decimal Price, string Currency, DeliveryZone Zone, ShipmentStatus Status, DateTimeOffset CreatedAtUtc);
public sealed record QuoteDto(decimal Price, string Currency, DeliveryZone Zone, decimal BillableWeightKg);
public sealed record TariffDto(DeliveryZone Zone, decimal BasePrice, decimal PricePerKg, string Currency);
public sealed record CreateShipment(AddressInput Sender, AddressInput Recipient, ParcelInput Parcel) : IRequest<ShipmentDto>;
public sealed record QuoteShipment(AddressInput Sender, AddressInput Recipient, ParcelInput Parcel) : IRequest<QuoteDto>;
public sealed record GetShipment(Guid ShipmentId) : IRequest<ShipmentDto>;
public sealed record ListShipments(int Offset = 0, int Limit = 20) : IRequest<IReadOnlyList<ShipmentDto>>;
public sealed record GetLabel(Guid ShipmentId) : IRequest<byte[]>;
public sealed record ListTariffs : IRequest<IReadOnlyList<TariffDto>>;
public sealed record UpdateTariff(DeliveryZone Zone, decimal BasePrice, decimal PricePerKg) : IRequest<TariffDto>;

public static class ShipmentMapping
{
    public static ShippingAddress ToDomain(this AddressInput value) => new(value.Name, value.Email, value.Phone, value.Line1, value.Line2, value.PostalCode, value.City, value.CountryCode);
    public static ParcelSpecification ToDomain(this ParcelInput value) => new(value.WeightKg, value.LengthCm, value.WidthCm, value.HeightCm);
    public static AddressInput ToDto(this ShippingAddress value) => new(value.Name, value.Email, value.Phone, value.Line1, value.Line2, value.PostalCode, value.City, value.CountryCode);
    public static ShipmentDto ToDto(this ParcelShipment value) => new(value.Id, value.TrackingNumber, value.OwnerSubject,
        value.Sender.ToDto(), value.Recipient.ToDto(), new(value.Parcel.WeightKg, value.Parcel.LengthCm, value.Parcel.WidthCm, value.Parcel.HeightCm),
        value.Price, value.Currency, value.Zone, value.Status, value.CreatedAtUtc);
    public static TariffDto ToDto(this Tariff value) => new(value.Zone, value.BasePrice, value.PricePerKg, value.Currency);
}
