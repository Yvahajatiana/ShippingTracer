using Asp.Versioning;
using MediatR;
using ParcelFlow.Notification.Application;

namespace ParcelFlow.Notification.Api;

public sealed class HttpNotificationActor(IHttpContextAccessor accessor) : INotificationActor
{
    public string Subject => accessor.HttpContext?.User.FindFirst("sub")?.Value ?? string.Empty;
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("admin") == true;
}

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var notifications = app.MapGroup("/api/v{version:apiVersion}/notifications")
            .WithApiVersionSet(versions).MapToApiVersion(1, 0).WithTags("Notifications")
            .RequireAuthorization("notification-users");
        notifications.MapGet("", async (ISender sender, CancellationToken ct, Guid? shipmentId = null, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListNotifications(shipmentId, page, pageSize), ct)));
        notifications.MapGet("/{notificationId:guid}", async (Guid notificationId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetNotification(notificationId), ct)));
    }
}
