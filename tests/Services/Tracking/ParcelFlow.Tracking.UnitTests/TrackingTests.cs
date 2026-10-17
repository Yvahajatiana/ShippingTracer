using FluentAssertions;
using ParcelFlow.Tracking.Application;
using ParcelFlow.Tracking.Domain;
using Xunit;

namespace ParcelFlow.Tracking.UnitTests;

public sealed class TrackingTests
{
    private const string Number = "PF0123456789ABCDEF01234567";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Out_of_order_facts_have_deterministic_timeline_and_latest_status()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.InTransit, Start.AddHours(2), "LYON-01"));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Created, Start, null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.PickedUp, Start.AddHours(1), "PARIS-01"));
        record.Timeline.Select(x => x.Status).Should().Equal(ParcelStatus.Created, ParcelStatus.PickedUp, ParcelStatus.InTransit);
        record.CurrentStatus.Should().Be(ParcelStatus.InTransit);
        record.UpdatedAtUtc.Should().Be(Start.AddHours(2));
    }

    [Fact]
    public void Duplicate_event_is_noop_but_changed_payload_is_rejected()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        var id = Guid.NewGuid();
        record.AddEntry(new(id, ParcelStatus.PickedUp, Start, null)).Should().BeTrue();
        record.AddEntry(new(id, ParcelStatus.PickedUp, Start.ToOffset(TimeSpan.FromHours(2)), null)).Should().BeFalse();
        record.Revision.Should().Be(1);
        FluentActions.Invoking(() => record.AddEntry(new(id, ParcelStatus.Delivered, Start, null))).Should().Throw<InvalidOperationException>();
        record.Entries.Should().ContainSingle();
    }

    [Fact]
    public void Timestamp_precision_matches_postgresql_for_replayed_events()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        var id = Guid.NewGuid();
        var sourceTime = Start.AddTicks(1234567).ToOffset(TimeSpan.FromHours(2));
        var persistedTime = Start.AddTicks(1234560);
        record.AddEntry(new(id, ParcelStatus.PickedUp, sourceTime, null));
        record.Entries.Single().OccurredAtUtc.Should().Be(persistedTime);
        record.Entries.Single().OccurredAtUtc.Offset.Should().Be(TimeSpan.Zero);
        record.AddEntry(new(id, ParcelStatus.PickedUp, persistedTime, null)).Should().BeFalse();
        record.AddEntry(new(id, ParcelStatus.PickedUp, sourceTime, null)).Should().BeFalse();
        record.Revision.Should().Be(1);
    }

    [Fact]
    public void Delivered_can_only_advance_to_returned_and_all_facts_are_retained()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Delivered, Start, null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Failed, Start.AddHours(1), null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Created, Start.AddHours(2), null));
        record.CurrentStatus.Should().Be(ParcelStatus.Delivered);
        record.UpdatedAtUtc.Should().Be(Start);
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Returned, Start.AddHours(3), null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Delivered, Start.AddHours(4), null));
        record.CurrentStatus.Should().Be(ParcelStatus.Returned);
        record.UpdatedAtUtc.Should().Be(Start.AddHours(3));
        record.Entries.Should().HaveCount(5);
    }

    [Fact]
    public void Created_does_not_regress_an_existing_state()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.PickedUp, Start, null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Created, Start.AddDays(1), null));
        record.CurrentStatus.Should().Be(ParcelStatus.PickedUp);
        record.UpdatedAtUtc.Should().Be(Start);
    }

    [Fact]
    public void Ties_use_semantic_rank_then_event_id()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        record.AddEntry(new(second, ParcelStatus.Failed, Start, null));
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.Delivered, Start, null));
        record.AddEntry(new(first, ParcelStatus.Failed, Start, null));
        record.Timeline.Take(2).Select(x => x.EventId).Should().Equal(first, second);
        record.CurrentStatus.Should().Be(ParcelStatus.Delivered);
    }

    [Fact]
    public void Register_normalizes_and_prevents_reassignment()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.Register(" " + Number.ToLowerInvariant() + " ");
        record.TrackingNumber.Should().Be(Number);
        record.Register(Number);
        record.Revision.Should().Be(1);
        FluentActions.Invoking(() => record.Register("PF111111111111111111111111")).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("PF0123\n456789ABCDEF01234567")]
    [InlineData("PF0123456789ABCDEF0123456G")]
    [InlineData("")]
    public void Invalid_tracking_numbers_are_rejected(string value)
    {
        FluentActions.Invoking(() => new TrackingRecord(Guid.NewGuid()).Register(value)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("PARIS\n")]
    [InlineData("paris")]
    [InlineData("ÉVRY")]
    [InlineData("-PARIS")]
    public void Facility_codes_are_bounded_ascii_identifiers(string value) =>
        FluentActions.Invoking(() => new TrackingEntry(Guid.NewGuid(), ParcelStatus.Created, Start, value)).Should().Throw<ArgumentException>();

    [Fact]
    public void Entry_rejects_missing_identity_time_and_invalid_status()
    {
        FluentActions.Invoking(() => new TrackingRecord(Guid.Empty)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new TrackingEntry(Guid.Empty, ParcelStatus.Created, Start, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new TrackingEntry(Guid.NewGuid(), ParcelStatus.Created, default, null)).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new TrackingEntry(Guid.NewGuid(), (ParcelStatus)100, Start, null)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Public_dto_exposes_only_tracking_facts_and_french_labels()
    {
        var record = new TrackingRecord(Guid.NewGuid());
        record.Register(Number);
        record.AddEntry(new(Guid.NewGuid(), ParcelStatus.PickedUp, Start, "PARIS-01"));
        var dto = record.ToPublicDto();
        dto.CurrentStatusLabel.Should().Be("Pris en charge");
        dto.History.Single().StatusLabel.Should().Be("Pris en charge");
        typeof(PublicTrackingDto).GetProperties().Select(x => x.Name).Should()
            .Equal("TrackingNumber", "CurrentStatus", "CurrentStatusLabel", "UpdatedAtUtc", "History");
        typeof(PublicTrackingEntryDto).GetProperties().Select(x => x.Name).Should()
            .Equal("Status", "StatusLabel", "OccurredAtUtc", "FacilityCode");
        Enum.GetValues<ParcelStatus>().Select(x => x.Label()).Should().Equal("Créé", "Pris en charge", "En transit", "En centre de tri", "En livraison", "Livré", "Échec", "Retour");
    }

    [Fact]
    public void Validation_handles_nulls_and_invalid_identifiers()
    {
        new GetPublicTrackingValidator().Validate(new GetPublicTracking(null!)).IsValid.Should().BeFalse();
        new GetShipmentTrackingValidator().Validate(new GetShipmentTracking(Guid.Empty)).IsValid.Should().BeFalse();
        new RecordTrackingEventValidator().Validate(new RecordTrackingEvent(Guid.Empty, Guid.Empty, "bad", (ParcelStatus)99, default, "bad")).Errors.Should().HaveCount(6);
    }
}
