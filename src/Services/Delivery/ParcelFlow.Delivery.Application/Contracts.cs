using MediatR;
using ParcelFlow.Delivery.Domain;

namespace ParcelFlow.Delivery.Application;

public interface IDeliveryActor { string Subject { get; } bool IsAdmin { get; } }
public interface IDeliveryCommand { }
public interface IDeliveryStore
{
    Task LockRouteAsync(Guid id, CancellationToken cancellationToken);
    Task LockParcelAsync(Guid id, CancellationToken cancellationToken);
    Task LockDriverAsync(string subject, CancellationToken cancellationToken);
    Task<DeliveryRoute?> FindRouteAsync(Guid id, CancellationToken cancellationToken);
    Task<DeliveryParcel?> FindParcelAsync(Guid id, CancellationToken cancellationToken);
    Task<DeliveryDriver?> FindDriverAsync(string subject, CancellationToken cancellationToken);
    Task<DeliveryAttempt?> FindAttemptAsync(Guid operationId, CancellationToken cancellationToken);
    Task<DeliveryAttempt?> FindAttemptByStopAsync(Guid routeId, Guid shipmentId, CancellationToken cancellationToken);
    Task<bool> HasOpenRoutesAsync(string subject, CancellationToken cancellationToken);
    void AddDriver(DeliveryDriver driver);
    void AddRoute(DeliveryRoute route);
    void AddParcel(DeliveryParcel parcel);
    void AddAttempt(DeliveryAttempt attempt);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<RoutePageDto> ListRoutesAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<DriverPageDto> ListDriversAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<AvailableParcelPageDto> ListAvailableParcelsAsync(int page, int pageSize, CancellationToken cancellationToken);
}
public sealed record CreateDriver(string Subject, string DisplayName) : IRequest<DriverDto>, IDeliveryCommand;
public sealed record UpdateDriver(string Subject, string DisplayName, bool IsActive) : IRequest<DriverDto>, IDeliveryCommand;
public sealed record CreateRoute(Guid Id, string Name, DateOnly ServiceDate) : IRequest<RouteDto>, IDeliveryCommand;
public sealed record AssignRouteDriver(Guid RouteId, string DriverSubject) : IRequest<RouteDto>, IDeliveryCommand;
public sealed record AddRouteParcel(Guid RouteId, Guid ShipmentId) : IRequest<RouteDto>, IDeliveryCommand;
public sealed record RemoveRouteParcel(Guid RouteId, Guid ShipmentId) : IRequest<RouteDto>, IDeliveryCommand;
public sealed record StartRoute(Guid RouteId) : IRequest<RouteDto>, IDeliveryCommand;
public sealed record RegisterReadyShipment(Guid ShipmentId, string TrackingNumber) : IRequest<bool>, IDeliveryCommand;
public sealed record RegisterParcelPickup(Guid ShipmentId) : IRequest<bool>, IDeliveryCommand;
public sealed record ConfirmDelivery(Guid OperationId, Guid RouteId, Guid ShipmentId, string RecipientName, byte[]? SignaturePng, byte[]? Photo) : IRequest<AttemptDto>, IDeliveryCommand;
public sealed record FailDelivery(Guid OperationId, Guid RouteId, Guid ShipmentId, DeliveryFailureReason Reason) : IRequest<AttemptDto>, IDeliveryCommand;
public sealed record GetRoute(Guid RouteId) : IRequest<RouteDto>;
public sealed record ListRoutes(int Page = 1, int PageSize = 50) : IRequest<RoutePageDto>;
public sealed record ListDrivers(int Page = 1, int PageSize = 50) : IRequest<DriverPageDto>;
public sealed record ListAvailableParcels(int Page = 1, int PageSize = 50) : IRequest<AvailableParcelPageDto>;
public sealed record GetDeliveryProof(Guid RouteId, Guid ShipmentId) : IRequest<AttemptDto>;
public enum ProofImageKind { Signature, Photo }
public sealed record GetDeliveryProofImage(Guid RouteId, Guid ShipmentId, ProofImageKind Kind) : IRequest<ProofImageDto>;
public sealed record DriverDto(string Subject, string DisplayName, bool IsActive);
public sealed record DriverPageDto(int Page, int PageSize, int Total, IReadOnlyList<DriverDto> Items);
public sealed record StopDto(Guid ShipmentId, int Sequence, DeliveryStopStatus Status, DeliveryFailureReason? FailureReason, DateTimeOffset? CompletedAtUtc);
public sealed record RouteDto(Guid Id, string Name, DateOnly ServiceDate, string? DriverSubject, DeliveryRouteStatus Status, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, IReadOnlyList<StopDto> Stops);
public sealed record RoutePageDto(int Page, int PageSize, int Total, IReadOnlyList<RouteDto> Items);
public sealed record AvailableParcelDto(Guid ShipmentId, string TrackingNumber);
public sealed record AvailableParcelPageDto(int Page, int PageSize, int Total, IReadOnlyList<AvailableParcelDto> Items);
public sealed record AttemptDto(Guid OperationId, Guid RouteId, Guid ShipmentId, DeliveryOutcome Outcome, DateTimeOffset OccurredAtUtc, string ActorSubject, string? RecipientName, DeliveryFailureReason? FailureReason, bool HasSignature, bool HasPhoto);
public sealed record ProofImageDto(byte[] Content, string ContentType);
public sealed class DeliveryNotFoundException(string message) : Exception(message);
public sealed class DeliveryConflictException(string message) : Exception(message);
public sealed class DeliveryForbiddenException(string message) : Exception(message);
public static class DeliveryMapping
{
    public static DriverDto ToDto(this DeliveryDriver value) => new(value.Subject, value.DisplayName, value.IsActive);
    public static RouteDto ToDto(this DeliveryRoute value) => new(value.Id, value.Name, value.ServiceDate, value.DriverSubject, value.Status, value.StartedAtUtc, value.CompletedAtUtc, value.Stops.OrderBy(x => x.Sequence).Select(x => new StopDto(x.ShipmentId, x.Sequence, x.Status, x.FailureReason, x.CompletedAtUtc)).ToArray());
    public static AttemptDto ToDto(this DeliveryAttempt value) => new(value.OperationId, value.RouteId, value.ShipmentId, value.Outcome, value.OccurredAtUtc, value.ActorSubject, value.RecipientName, value.FailureReason, value.SignaturePng is not null, value.Photo is not null);
}
