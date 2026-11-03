using System.Reflection;
using FluentAssertions;
using FluentValidation;
using MassTransit;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Identity;
using ParcelFlow.BuildingBlocks.Contracts.Reporting.V1;
using ParcelFlow.Reporting.Application;
using ParcelFlow.Reporting.Domain;
using Xunit;

namespace ParcelFlow.Reporting.UnitTests;

public sealed class ReportingTests
{
    private const string Tracking = "PF0123456789ABCDEF01234567";
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Out_of_order_facts_recompute_earliest_times_and_precise_durations()
    {
        var root = Root();
        Add(root, ParcelStatus.Delivered, Start.AddHours(30));
        Add(root, ParcelStatus.PickedUp, Start.AddHours(6));
        Add(root, ParcelStatus.Created, Start.AddHours(2));
        Add(root, ParcelStatus.Created, Start.AddTicks(17));
        root.CurrentStatus.Should().Be(ParcelStatus.Delivered);
        root.CreatedAtUtc.Should().Be(Start.AddTicks(10));
        root.DeliverySeconds.Should().Be(108000m - 0.000001m);
        root.TransitSeconds.Should().Be(86400m);
        root.Revision.Should().Be(4);
    }

    [Fact]
    public void Terminal_delivery_ignores_later_failure_but_return_excludes_durations()
    {
        var root = Root(); Add(root, ParcelStatus.Created, Start); Add(root, ParcelStatus.Delivered, Start.AddHours(2));
        Add(root, ParcelStatus.Failed, Start.AddHours(3)); Add(root, ParcelStatus.Created, Start.AddHours(4));
        root.CurrentStatus.Should().Be(ParcelStatus.Delivered); root.DeliverySeconds.Should().Be(7200m);
        Add(root, ParcelStatus.Returned, Start.AddHours(5)); Add(root, ParcelStatus.Delivered, Start.AddHours(6));
        root.CurrentStatus.Should().Be(ParcelStatus.Returned); root.DeliverySeconds.Should().BeNull(); root.TransitSeconds.Should().BeNull();
    }

    [Fact]
    public void Ties_use_semantic_rank_and_source_id_rather_than_transport_envelope()
    {
        var root = Root();
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        root.AddFact(new StatusFact(high, low, root.ShipmentId, ParcelStatus.Delivered, Start, null));
        root.AddFact(new StatusFact(low, high, root.ShipmentId, ParcelStatus.Failed, Start, null));
        root.CurrentStatus.Should().Be(ParcelStatus.Delivered);
        Add(root, ParcelStatus.Returned, Start); root.CurrentStatus.Should().Be(ParcelStatus.Returned);
    }

    [Fact]
    public void Missing_or_negative_time_pairs_are_excluded_independently()
    {
        var root = Root(); Add(root, ParcelStatus.Delivered, Start);
        root.DeliverySeconds.Should().BeNull(); root.TransitSeconds.Should().BeNull();
        Add(root, ParcelStatus.Created, Start.AddHours(1)); Add(root, ParcelStatus.PickedUp, Start.AddHours(-1));
        root.DeliverySeconds.Should().BeNull(); root.TransitSeconds.Should().Be(3600m);
        var zero = Root(); Add(zero, ParcelStatus.Created, Start); Add(zero, ParcelStatus.Delivered, Start);
        zero.DeliverySeconds.Should().Be(0m);
    }

    [Fact]
    public void Source_replay_is_immutable_even_with_another_envelope_and_UTC_offset()
    {
        var root = Root(); var first = new StatusFact(Guid.NewGuid(), Guid.NewGuid(), root.ShipmentId, ParcelStatus.InTransit, Start.AddTicks(1), "LYON-01");
        root.AddFact(first).Should().BeTrue();
        root.AddFact(new StatusFact(Guid.NewGuid(), first.SourceEventId, root.ShipmentId, first.Status, Start.ToOffset(TimeSpan.FromHours(2)).AddTicks(9), first.FacilityCode)).Should().BeFalse();
        root.Revision.Should().Be(1); root.Facts.Should().ContainSingle();
        FluentActions.Invoking(() => root.AddFact(new StatusFact(Guid.NewGuid(), first.SourceEventId, root.ShipmentId, ParcelStatus.Failed, Start, first.FacilityCode))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => root.AddFact(new StatusFact(first.EventId, Guid.NewGuid(), root.ShipmentId, first.Status, Start, first.FacilityCode))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => root.AddFact(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), first.Status, Start, null))).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("pf0123456789abcdef01234567")]
    [InlineData("PF0123456789ABCDEF01234567\n")]
    [InlineData("")]
    public void Invalid_tracking_numbers_are_rejected(string tracking) =>
        FluentActions.Invoking(() => new ShipmentMetrics(Guid.NewGuid(), tracking)).Should().Throw<ArgumentException>();

    [Fact]
    public void Domain_guards_event_ids_dates_enum_and_facility()
    {
        FluentActions.Invoking(() => new ShipmentMetrics(Guid.Empty, Tracking)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new StatusFact(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), ParcelStatus.Created, Start, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new StatusFact(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), ParcelStatus.Created, Start, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, ParcelStatus.Created, Start, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), (ParcelStatus)42, Start, null)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ParcelStatus.Created, default, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new StatusFact(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ParcelStatus.Created, Start, "LYON\n")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task New_fact_locks_then_publishes_metadata_and_saves_once()
    {
        var f = Fixture(); var request = Command();
        (await f.Handler.Handle(request, default)).Should().BeTrue();
        f.Store.Locked.Should().Be((request.EventId, request.SourceEventId, request.ShipmentId));
        f.Store.Saves.Should().Be(1);
        var published = f.Publisher.Events.Should().ContainSingle().Which.Should().BeOfType<ShipmentMetricsUpdated>().Subject;
        published.ShipmentId.Should().Be(request.ShipmentId); published.SourceEventId.Should().Be(request.SourceEventId);
        // The incoming TrackingStatusChanged correlation is propagated, not replaced by the shipment identifier.
        published.CorrelationId.Should().Be(f.Flow).And.NotBe(request.ShipmentId); published.EventId.Should().NotBe(request.EventId);
        published.EventId.Should().Be(DeterministicGuid.Create("reporting:metrics-updated:", request.SourceEventId));
        published.OccurredAtUtc.Should().Be(Start.AddDays(1));
        f.Store.Shipments.Single().TrackingNumber.Should().Be(Tracking);
    }

    [Fact]
    public async Task Replays_preserve_facts_and_publications_and_remember_fresh_envelopes()
    {
        var f = Fixture(); var request = Command(); await f.Handler.Handle(request, default);
        (await f.Handler.Handle(request, default)).Should().BeFalse();
        (await f.Handler.Handle(request with { EventId = Guid.NewGuid(), OccurredAtUtc = request.OccurredAtUtc.AddTicks(9) }, default)).Should().BeFalse();
        f.Store.Saves.Should().Be(2); f.Publisher.Events.Should().ContainSingle();
        f.Store.Shipments.Single().Revision.Should().Be(1);
        f.Store.Shipments.Single().Facts.Should().ContainSingle();
        f.Store.Envelopes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Accepted_replay_envelope_cannot_be_reused_for_another_source()
    {
        var f = Fixture(); var first = Command(); await f.Handler.Handle(first, default);
        var replay = first with { EventId = Guid.NewGuid() };
        (await f.Handler.Handle(replay, default)).Should().BeFalse();
        (await f.Handler.Handle(replay, default)).Should().BeFalse();
        await FluentActions.Awaiting(() => f.Handler.Handle(replay with { SourceEventId = Guid.NewGuid() }, default))
            .Should().ThrowAsync<ReportingConflictException>();
        f.Store.Saves.Should().Be(2); f.Publisher.Events.Should().ContainSingle();
        f.Store.Shipments.Single().Facts.Should().ContainSingle();
        f.Store.Shipments.Single().Revision.Should().Be(1);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("envelope")]
    [InlineData("shipment")]
    [InlineData("tracking")]
    [InlineData("facility")]
    [InlineData("date")]
    public async Task Global_identifier_and_immutable_details_conflicts_are_rejected(string mutation)
    {
        var f = Fixture(); var request = Command(); await f.Handler.Handle(request, default);
        var changed = mutation switch
        {
            "source" => request with { EventId = Guid.NewGuid(), Status = ParcelStatus.Failed },
            "envelope" => request with { SourceEventId = Guid.NewGuid() },
            "shipment" => request with { ShipmentId = Guid.NewGuid() },
            "tracking" => request with { TrackingNumber = "PFABCDEF0123456789ABCDEF01" },
            "facility" => request with { FacilityCode = "PARIS-01" },
            _ => request with { OccurredAtUtc = request.OccurredAtUtc.AddSeconds(1) }
        };
        await FluentActions.Awaiting(() => f.Handler.Handle(changed, default)).Should().ThrowAsync<ReportingConflictException>();
        f.Store.Saves.Should().Be(1); f.Publisher.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task New_source_cannot_change_existing_shipment_tracking_number()
    {
        var f = Fixture(); var request = Command(); await f.Handler.Handle(request, default);
        await FluentActions.Awaiting(() => f.Handler.Handle(request with { EventId = Guid.NewGuid(), SourceEventId = Guid.NewGuid(), TrackingNumber = "PFABCDEF0123456789ABCDEF01" }, default)).Should().ThrowAsync<ReportingConflictException>();
    }

    [Fact]
    public async Task Invalid_direct_command_does_not_lock_or_publish_and_publication_failure_prevents_save()
    {
        var f = Fixture(); var request = Command();
        await FluentActions.Awaiting(() => f.Handler.Handle(request with { SourceEventId = Guid.Empty }, default)).Should().ThrowAsync<ValidationException>();
        f.Store.Locked.Should().BeNull(); f.Publisher.Events.Should().BeEmpty();
        f.Publisher.Failure = new InvalidOperationException("broker unavailable");
        await FluentActions.Awaiting(() => f.Handler.Handle(request, default)).Should().ThrowAsync<InvalidOperationException>();
        f.Store.Saves.Should().Be(0);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(366, true)]
    [InlineData(367, false)]
    public void Query_interval_is_exclusive_and_bounded(int days, bool valid)
    {
        var from = new DateOnly(2026, 1, 1);
        new GetKpisValidator().Validate(new GetKpis(from, from.AddDays(days))).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task Query_handler_validates_direct_calls_and_passes_normalized_generation_time()
    {
        var f = Fixture(); var handler = new GetKpisHandler(f.Store, new Clock()); var from = new DateOnly(2026, 10, 1); var to = from.AddDays(31);
        var dto = await handler.Handle(new GetKpis(from, to), default);
        dto.From.Should().Be(from); dto.To.Should().Be(to); dto.GeneratedAtUtc.Should().Be(Start.AddDays(1));
        await FluentActions.Awaiting(() => handler.Handle(new GetKpis(to, from), default)).Should().ThrowAsync<ValidationException>();
        f.Store.Queries.Should().Be(1);
    }

    [Fact]
    public void Command_validator_rejects_unknown_status_and_control_characters()
    {
        var valid = Command(); var validator = new RecordStatusFactValidator();
        foreach (var request in new[] { valid with { EventId = Guid.Empty }, valid with { ShipmentId = Guid.Empty }, valid with { Status = (ParcelStatus)99 },
                     valid with { FacilityCode = "PARIS\n" }, valid with { TrackingNumber = Tracking + "\n" }, valid with { OccurredAtUtc = default } })
            validator.Validate(request).IsValid.Should().BeFalse();
    }

    private static ShipmentMetrics Root() => new(Guid.NewGuid(), Tracking);
    private static void Add(ShipmentMetrics root, ParcelStatus status, DateTimeOffset at) => root.AddFact(new StatusFact(Guid.NewGuid(), Guid.NewGuid(), root.ShipmentId, status, at, null));
    private static RecordStatusFact Command() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Tracking, ParcelStatus.Created, Start, null);
    private static F Fixture()
    {
        var publisher = DispatchProxy.Create<IPublishEndpoint, Publisher>(); var store = new Store(); var flow = Guid.NewGuid();
        return new F(store, (Publisher)publisher, new RecordStatusFactHandler(store, publisher, new Clock(), CorrelationContext.From(flow)), flow);
    }
    private sealed record F(Store Store, Publisher Publisher, RecordStatusFactHandler Handler, Guid Flow);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Start.AddDays(1).AddTicks(9); }
    public class Publisher : DispatchProxy
    {
        public List<object> Events { get; } = []; public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (Failure is not null) return Task.FromException(Failure);
            Events.Add(args![0]!); return Task.CompletedTask;
        }
    }
    private sealed class Store : IReportingStore
    {
        public List<ShipmentMetrics> Shipments { get; } = [];
        public Dictionary<Guid, Guid> Envelopes { get; } = [];
        public (Guid, Guid, Guid)? Locked { get; private set; }
        public int Saves { get; private set; } public int Queries { get; private set; }
        public Task LockAsync(Guid eventId, Guid sourceEventId, Guid shipmentId, CancellationToken ct) { Locked = (eventId, sourceEventId, shipmentId); return Task.CompletedTask; }
        public Task<StatusFact?> FindFactByEventIdAsync(Guid eventId, CancellationToken ct) => Task.FromResult(Shipments.SelectMany(x => x.Facts).SingleOrDefault(x => x.EventId == eventId || Envelopes.GetValueOrDefault(eventId) == x.SourceEventId));
        public Task<StatusFact?> FindFactBySourceIdAsync(Guid sourceEventId, CancellationToken ct) => Task.FromResult(Shipments.SelectMany(x => x.Facts).SingleOrDefault(x => x.SourceEventId == sourceEventId));
        public Task<ShipmentMetrics?> FindShipmentAsync(Guid shipmentId, CancellationToken ct) => Task.FromResult(Shipments.SingleOrDefault(x => x.ShipmentId == shipmentId));
        public void AddShipment(ShipmentMetrics shipment) => Shipments.Add(shipment);
        public void RememberEnvelope(Guid eventId, Guid sourceEventId) => Envelopes.Add(eventId, sourceEventId);
        public Task SaveChangesAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }
        public Task<KpiReportDto> GetKpisAsync(DateOnly from, DateOnly to, DateTimeOffset generatedAtUtc, CancellationToken ct)
        {
            Queries++; return Task.FromResult(new KpiReportDto(from, to, generatedAtUtc, 0, 0, null, null, 0, null, 0, [], []));
        }
    }
}

