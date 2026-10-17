using FluentAssertions;
using ParcelFlow.Warehouse.Domain;
using Xunit;

namespace ParcelFlow.Warehouse.UnitTests;

public sealed class DomainTests
{
    private const string Number = "PF0123456789ABCDEF01234567";
    private static readonly DateTimeOffset Start = new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero);
    public static WarehouseSite Site(WarehouseKind kind = WarehouseKind.Warehouse) => new(Guid.NewGuid(), " par-01 ", " Paris ", kind, " 5 rue des Entrepots ", " Paris ", " 75012 ", " fr ");

    [Fact]
    public void Warehouse_normalizes_identifiers_and_preserves_identity_during_update()
    {
        var site = Site();
        site.Code.Should().Be("PAR-01"); site.CountryCode.Should().Be("FR"); site.Name.Should().Be("Paris");
        var id = site.Id;
        site.Update("Lyon", "8 rue des Docks", "Lyon", "69002", "FR", false);
        site.Id.Should().Be(id); site.Code.Should().Be("PAR-01"); site.Kind.Should().Be(WarehouseKind.Warehouse);
        site.IsActive.Should().BeFalse();
    }
    [Theory]
    [InlineData("BAD/CODE")]
    [InlineData("éspace")]
    [InlineData("")]
    public void Invalid_facility_code_is_rejected(string code) => FluentActions.Invoking(() =>
        new WarehouseSite(Guid.NewGuid(), code, "Paris", WarehouseKind.Warehouse, "Street", "Paris", "75012", "FR")).Should().Throw<ArgumentException>();
    [Fact]
    public void Entry_exit_and_transfer_preserve_one_location_and_pickup_only_once()
    {
        var parcel = new ParcelInventory(Guid.NewGuid(), Number.ToLowerInvariant());
        var first = Site(); var next = Site(WarehouseKind.SortingCenter);
        parcel.ApplyScan(first, ScanDirection.Entry, Start).Should().BeTrue();
        parcel.CurrentWarehouseId.Should().Be(first.Id);
        FluentActions.Invoking(() => parcel.ApplyScan(next, ScanDirection.Entry, Start.AddSeconds(1))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => parcel.ApplyScan(next, ScanDirection.Exit, Start.AddSeconds(1))).Should().Throw<InvalidOperationException>();
        parcel.Revision.Should().Be(1);
        parcel.ApplyScan(first, ScanDirection.Exit, Start.AddSeconds(2)).Should().BeFalse();
        parcel.CurrentWarehouseId.Should().BeNull();
        parcel.ApplyScan(next, ScanDirection.Entry, Start.AddSeconds(3)).Should().BeFalse();
        parcel.CurrentWarehouseId.Should().Be(next.Id); parcel.HasBeenPickedUp.Should().BeTrue(); parcel.Revision.Should().Be(3);
    }
    [Fact]
    public void Inactive_warehouse_rejects_entry_without_mutating_inventory()
    {
        var site = Site(); site.Update(site.Name, site.Street, site.City, site.PostalCode, site.CountryCode, false);
        var parcel = new ParcelInventory(Guid.NewGuid(), Number);
        FluentActions.Invoking(() => parcel.ApplyScan(site, ScanDirection.Entry, Start)).Should().Throw<InvalidOperationException>();
        parcel.HasBeenPickedUp.Should().BeFalse(); parcel.LastScanAtUtc.Should().BeNull(); parcel.Revision.Should().Be(0);
    }
    [Fact]
    public void Scan_dates_are_utc_microseconds_and_cannot_go_backwards()
    {
        var parcel = new ParcelInventory(Guid.NewGuid(), Number); var site = Site();
        parcel.ApplyScan(site, ScanDirection.Entry, Start.ToOffset(TimeSpan.FromHours(2)).AddTicks(19));
        parcel.LastScanAtUtc.Should().Be(Start.AddTicks(10)); parcel.LastScanAtUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        FluentActions.Invoking(() => parcel.ApplyScan(site, ScanDirection.Exit, Start)).Should().Throw<InvalidOperationException>();
        parcel.CurrentWarehouseId.Should().Be(site.Id); parcel.Revision.Should().Be(1);
    }
    [Fact]
    public void Invalid_update_cannot_partially_modify_warehouse()
    {
        var site = Site();
        FluentActions.Invoking(() => site.Update("Changed", "Changed", "Changed", "Changed", "FRA", false)).Should().Throw<ArgumentException>();
        site.Name.Should().Be("Paris"); site.City.Should().Be("Paris"); site.IsActive.Should().BeTrue();
    }
    [Fact]
    public void Immutable_scan_preserves_normalized_audit_fields()
    {
        var scan = new WarehouseScan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " " + Number.ToLowerInvariant() + " ", ScanDirection.Exit, Start.AddTicks(19), " operator-42 ");
        scan.TrackingNumber.Should().Be(Number); scan.OperatorSubject.Should().Be("operator-42"); scan.OccurredAtUtc.Should().Be(Start.AddTicks(10));
    }
}
