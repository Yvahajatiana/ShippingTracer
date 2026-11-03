using System.Reflection;
using FluentAssertions;
using FluentValidation;
using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Notification.V1;
using ParcelFlow.Notification.Application;
using ParcelFlow.Notification.Domain;
using Xunit;

namespace ParcelFlow.Notification.UnitTests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Observed = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Tracking = "PF0123456789ABCDEF01234567";
    [Theory]
    [InlineData(NotificationKind.ShipmentCreated, "créé")]
    [InlineData(NotificationKind.TrackingStatus, "Livré")]
    [InlineData(NotificationKind.InvoiceIssued, "12.34 EUR")]
    [InlineData(NotificationKind.PaymentCompleted, "accepté")]
    [InlineData(NotificationKind.PaymentFailed, "refusé")]
    [InlineData(NotificationKind.CreditNoteIssued, "12.34 EUR")]
    public async Task Every_business_event_records_one_mock_receipt_per_channel(NotificationKind kind, string content)
    {
        var f = Fixture(); var command = Command(kind);
        (await f.Handler.Handle(command, default)).Should().BeTrue();
        var root = f.Store.Events.Single(); root.Messages.Should().HaveCount(2);
        root.Messages.Select(x => x.Channel).Should().BeEquivalentTo(new[] { NotificationChannel.Email, NotificationChannel.Sms });
        root.Messages.Should().OnlyContain(x => x.DeliveryStatus == "MockSent" && x.Body.Contains(content) && x.Body.Contains("Événement observé à") &&
            x.CustomerSubject == "alice" && x.TrackingNumber == Tracking && x.OccurredAtUtc == Observed && x.RecordedAtUtc == Observed.AddHours(1));
        root.Messages.Single(x => x.Channel == NotificationChannel.Email).Destination.Should().Be("alice@example.test");
        root.Messages.Single(x => x.Channel == NotificationChannel.Sms).Destination.Should().Be("+33 (0)6 12 34 56 78");
        f.Publisher.Events.Should().HaveCount(2).And.OnlyContain(x => x is NotificationRecorded);
        var receipts = f.Publisher.Events.Cast<NotificationRecorded>().ToArray();
        receipts.Should().OnlyContain(x => x.SourceEventId == command.EventId && x.ShipmentId == command.ShipmentId && x.CorrelationId == f.Flow && x.CustomerSubject == "alice");
        receipts.Select(x => x.NotificationId).Should().BeEquivalentTo(root.Messages.Select(x => x.Id));
        receipts.Select(x => x.EventId).Should().BeEquivalentTo(root.Messages.Select(x => DeterministicGuid.Create("notification:recorded:", x.Id)));
        f.Store.Saves.Should().Be(1); f.Resolver.Calls.Should().Be(1); f.Store.Locked.Should().ContainSingle().Which.Should().Be(command.EventId);
    }
    [Theory]
    [InlineData(ParcelStatus.Created, false)]
    [InlineData(ParcelStatus.PickedUp, true)]
    [InlineData(ParcelStatus.InTransit, true)]
    [InlineData(ParcelStatus.AtSortingCenter, true)]
    [InlineData(ParcelStatus.OutForDelivery, true)]
    [InlineData(ParcelStatus.Delivered, true)]
    [InlineData(ParcelStatus.Failed, true)]
    [InlineData(ParcelStatus.Returned, true)]
    public async Task Tracking_created_is_suppressed_and_other_historical_statuses_are_observations(ParcelStatus status, bool expected)
    {
        var f = Fixture(); var command = Command(NotificationKind.TrackingStatus) with { Status = status };
        (await f.Handler.Handle(command, default)).Should().Be(expected);
        f.Resolver.Calls.Should().Be(expected ? 1 : 0); f.Store.Saves.Should().Be(expected ? 1 : 0);
        if (!expected) { f.Store.Locked.Should().BeEmpty(); f.Publisher.Events.Should().BeEmpty(); }
    }
    [Fact]
    public async Task Replayed_event_is_noop_even_at_sub_microsecond_precision_and_without_contact_network()
    {
        var f = Fixture(); var command = Command() with { OccurredAtUtc = Observed.AddTicks(7).ToOffset(TimeSpan.FromHours(3)) };
        await f.Handler.Handle(command, default); f.Resolver.Failure = new HttpRequestException();
        (await f.Handler.Handle(command with { OccurredAtUtc = Observed }, default)).Should().BeFalse();
        f.Store.Events.Should().ContainSingle(); f.Publisher.Events.Should().HaveCount(2); f.Store.Saves.Should().Be(1); f.Resolver.Calls.Should().Be(1);
    }
    [Theory]
    [InlineData("shipment")]
    [InlineData("owner")]
    [InlineData("tracking")]
    [InlineData("time")]
    [InlineData("kind")]
    public async Task Reusing_source_event_with_different_details_conflicts_before_network(string changed)
    {
        var f = Fixture(); var command = Command(); await f.Handler.Handle(command, default);
        var replay = changed switch
        {
            "shipment" => command with { ShipmentId = Guid.NewGuid() },
            "owner" => command with { CustomerSubject = "bob" },
            "tracking" => command with { TrackingNumber = "PF1123456789ABCDEF01234567" },
            "time" => command with { OccurredAtUtc = Observed.AddTicks(10) },
            _ => command with { Kind = NotificationKind.PaymentFailed, CustomerSubject = null, TrackingNumber = null }
        };
        await FluentActions.Awaiting(() => f.Handler.Handle(replay, default)).Should().ThrowAsync<NotificationConflictException>();
        f.Store.Saves.Should().Be(1); f.Resolver.Calls.Should().Be(1);
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("tracking")]
    public async Task Event_expected_owner_and_number_must_match_authoritative_snapshot(string changed)
    {
        var f = Fixture(); var command = Command();
        f.Resolver.Recipient = changed == "owner" ? f.Resolver.Recipient with { CustomerSubject = "bob" } : f.Resolver.Recipient with { TrackingNumber = "PF1123456789ABCDEF01234567" };
        await FluentActions.Awaiting(() => f.Handler.Handle(command, default)).Should().ThrowAsync<NotificationConflictException>();
        f.Store.Saves.Should().Be(0); f.Store.Events.Should().BeEmpty(); f.Publisher.Events.Should().BeEmpty();
    }
    [Fact]
    public async Task Resolver_failure_does_not_add_save_or_publish()
    {
        var f = Fixture(); f.Resolver.Failure = new HttpRequestException();
        await FluentActions.Awaiting(() => f.Handler.Handle(Command(), default)).Should().ThrowAsync<HttpRequestException>();
        f.Store.Saves.Should().Be(0); f.Store.Events.Should().BeEmpty(); f.Publisher.Events.Should().BeEmpty();
    }
    [Fact]
    public async Task Publish_failure_cannot_save_receipts()
    {
        var f = Fixture(); f.Publisher.Failure = new InvalidOperationException("broker unavailable");
        await FluentActions.Awaiting(() => f.Handler.Handle(Command(), default)).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Saves.Should().Be(0);
    }
    [Theory]
    [InlineData("+33 (0)6 12 34 56 78", true)]
    [InlineData("1", true)]
    [InlineData("", false)]
    [InlineData("123\n456", false)]
    [InlineData("1234567890123456789012345678901", false)]
    public void Phone_matches_shipping_snapshot_bounds(string phone, bool valid) => NotificationRules.IsPhone(phone).Should().Be(valid);
    [Theory]
    [InlineData("alice@example.test", true)]
    [InlineData("Alice <alice@example.test>", false)]
    [InlineData("not-email", false)]
    [InlineData("a@example.test\r\nInjected: x", false)]
    public void Email_is_an_address_without_header_injection(string email, bool valid) => NotificationRules.IsEmail(email).Should().Be(valid);
    [Fact]
    public async Task Invalid_metadata_is_rejected_even_on_suppressed_created_status()
    {
        var f = Fixture(); var invalid = Command(NotificationKind.TrackingStatus) with { Status = ParcelStatus.Created, ShipmentId = Guid.Empty };
        await FluentActions.Awaiting(() => f.Handler.Handle(invalid, default)).Should().ThrowAsync<ValidationException>();
        f.Store.Locked.Should().BeEmpty(); f.Resolver.Calls.Should().Be(0);
    }
    [Fact]
    public void Domain_receipts_are_immutable_bounded_and_not_reassigned()
    {
        var command = Command(); var root = Root(command);
        root.RecordMessages("alice", Tracking, "alice@example.test", "123", "Subject", "Body", "Body", Observed);
        FluentActions.Invoking(() => root.RecordMessages("alice", Tracking, "alice@example.test", "123", "Subject", "Body", "Body", Observed)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Root(command).RecordMessages("bob", Tracking, "bob@example.test", "123", "Subject", "Body", "Body", Observed)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Root(command).RecordMessages("alice", Tracking, "alice@example.test", "123", "Subject", "Body", new string('s', 321), Observed)).Should().Throw<ArgumentException>();
        root.Messages.Should().HaveCount(2); root.OccurredAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }
    [Theory]
    [InlineData("alice", false, true)]
    [InlineData("bob", false, false)]
    [InlineData("admin", true, true)]
    [InlineData("", true, false)]
    public async Task Only_owner_and_valid_admin_can_read_private_message(string subject, bool admin, bool allowed)
    {
        var f = Fixture(); await f.Handler.Handle(Command(), default); var message = f.Store.Events.Single().Messages.First();
        var handler = new GetNotificationHandler(f.Store, new Actor(subject, admin));
        if (allowed) (await handler.Handle(new GetNotification(message.Id), default)).Destination.Should().Be(message.Destination);
        else await FluentActions.Awaiting(() => handler.Handle(new GetNotification(message.Id), default)).Should().ThrowAsync<NotificationForbiddenException>();
    }
    [Fact]
    public async Task List_customer_is_scoped_admin_can_list_all_and_missing_message_is_404()
    {
        var f = Fixture(); await f.Handler.Handle(Command(), default);
        (await new ListNotificationsHandler(f.Store, new Actor("alice")).Handle(new ListNotifications(), default)).Total.Should().Be(2);
        f.Store.OwnerFilter.Should().Be("alice");
        (await new ListNotificationsHandler(f.Store, new Actor("bob")).Handle(new ListNotifications(), default)).Total.Should().Be(0);
        (await new ListNotificationsHandler(f.Store, new Actor("admin", true)).Handle(new ListNotifications(), default)).Total.Should().Be(2); f.Store.OwnerFilter.Should().BeNull();
        await FluentActions.Awaiting(() => new GetNotificationHandler(f.Store, new Actor("alice")).Handle(new GetNotification(Guid.NewGuid()), default)).Should().ThrowAsync<NotificationNotFoundException>();
        await FluentActions.Awaiting(() => new ListNotificationsHandler(f.Store, new Actor("")).Handle(new ListNotifications(), default)).Should().ThrowAsync<NotificationForbiddenException>();
    }
    [Theory]
    [InlineData(0, 50)]
    [InlineData(1000001, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Pagination_is_bounded(int page, int pageSize) => new ListNotificationsValidator().Validate(new ListNotifications(Page: page, PageSize: pageSize)).IsValid.Should().BeFalse();
    [Fact]
    public void Unknown_enums_control_text_and_irrelevant_financial_fields_are_rejected()
    {
        var validator = new RecordNotificationValidator(); var valid = Command();
        foreach (var invalid in new[] { valid with { Kind = (NotificationKind)99 }, valid with { CustomerSubject = "alice\nadmin" },
                     valid with { Amount = 1m }, valid with { TrackingNumber = Tracking.ToLowerInvariant() }, valid with { TrackingNumber = Tracking + "\n" },
                     Command(NotificationKind.TrackingStatus) with { Status = (ParcelStatus)99 },
                     Command(NotificationKind.InvoiceIssued) with { InvoiceId = Guid.NewGuid() }, Command(NotificationKind.PaymentCompleted) with { Currency = "USD" } })
            validator.Validate(invalid).IsValid.Should().BeFalse();
    }
    private static RecordNotification Command(NotificationKind kind = NotificationKind.ShipmentCreated)
    {
        var shipment = Guid.NewGuid();
        return new RecordNotification(Guid.NewGuid(), shipment, kind, Observed,
            kind is NotificationKind.ShipmentCreated or NotificationKind.InvoiceIssued or NotificationKind.CreditNoteIssued ? "alice" : null,
            kind is NotificationKind.ShipmentCreated or NotificationKind.TrackingStatus ? Tracking : null,
            kind == NotificationKind.TrackingStatus ? ParcelStatus.Delivered : null,
            kind is NotificationKind.InvoiceIssued or NotificationKind.CreditNoteIssued ? shipment : null,
            kind == NotificationKind.CreditNoteIssued ? Guid.NewGuid() : null,
            kind is NotificationKind.InvoiceIssued or NotificationKind.PaymentCompleted or NotificationKind.CreditNoteIssued ? 12.34m : null,
            kind is NotificationKind.InvoiceIssued or NotificationKind.PaymentCompleted or NotificationKind.CreditNoteIssued ? "EUR" : null);
    }
    private static NotificationEvent Root(RecordNotification command) => new(command.EventId, command.ShipmentId, command.Kind, command.OccurredAtUtc,
        command.CustomerSubject, command.TrackingNumber, command.Status, command.InvoiceId, command.CreditNoteId, command.Amount, command.Currency);
    private static F Fixture()
    {
        var endpoint = DispatchProxy.Create<IPublishEndpoint, Publisher>(); var store = new Store(); var resolver = new Resolver();
        // The consumed event's correlation, made ambient by the consume filter in production.
        var flow = Guid.NewGuid();
        return new F(store, resolver, (Publisher)endpoint, new RecordNotificationHandler(store, resolver, endpoint, new Clock(), CorrelationContext.From(flow)), flow);
    }
    private sealed record F(Store Store, Resolver Resolver, Publisher Publisher, RecordNotificationHandler Handler, Guid Flow);
    private sealed record Actor(string Subject, bool IsAdmin = false) : INotificationActor;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Observed.AddHours(1).AddTicks(7); }
    private sealed class Resolver : IShipmentRecipientResolver
    {
        public ShipmentRecipient Recipient { get; set; } = new("alice", Tracking, "alice@example.test", "+33 (0)6 12 34 56 78");
        public int Calls { get; private set; } public Exception? Failure { get; set; }
        public Task<ShipmentRecipient> ResolveAsync(Guid shipmentId, CancellationToken ct) { Calls++; return Failure is null ? Task.FromResult(Recipient) : Task.FromException<ShipmentRecipient>(Failure); }
    }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = []; public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) { if (Failure is not null) return Task.FromException(Failure); Events.Add(args![0]!); return Task.CompletedTask; }
    }
    private sealed class Store : INotificationStore
    {
        public List<NotificationEvent> Events { get; } = []; public List<Guid> Locked { get; } = [];
        public int Saves { get; private set; } public string? OwnerFilter { get; private set; }
        public Task LockEventAsync(Guid eventId, CancellationToken ct) { Locked.Add(eventId); return Task.CompletedTask; }
        public Task<NotificationEvent?> FindEventAsync(Guid eventId, CancellationToken ct) => Task.FromResult(Events.SingleOrDefault(x => x.EventId == eventId));
        public void AddEvent(NotificationEvent notificationEvent) => Events.Add(notificationEvent);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public Task<NotificationMessage?> FindNotificationAsync(Guid notificationId, CancellationToken ct) => Task.FromResult(Events.SelectMany(x => x.Messages).SingleOrDefault(x => x.Id == notificationId));
        public Task<NotificationPageDto> ListNotificationsAsync(string? ownerSubject, Guid? shipmentId, int page, int pageSize, CancellationToken ct)
        {
            OwnerFilter = ownerSubject;
            var items = Events.SelectMany(x => x.Messages).Where(x => (ownerSubject is null || x.CustomerSubject == ownerSubject) && (!shipmentId.HasValue || x.ShipmentId == shipmentId)).ToArray();
            return Task.FromResult(new NotificationPageDto(page, pageSize, items.Length, items.Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.ToDto()).ToArray()));
        }
    }
}
