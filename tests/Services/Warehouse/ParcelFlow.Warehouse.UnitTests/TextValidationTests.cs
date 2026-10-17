using FluentAssertions;
using ParcelFlow.Warehouse.Application;
using ParcelFlow.Warehouse.Domain;
using Xunit;

namespace ParcelFlow.Warehouse.UnitTests;

public sealed class TextValidationTests
{
    [Theory]
    [InlineData("Name")]
    [InlineData("Street")]
    [InlineData("City")]
    [InlineData("PostalCode")]
    public void Text_fields_reject_nul_in_create_and_update_validation_and_domain(string field)
    {
        var create = new CreateWarehouse(Guid.NewGuid(), "PAR-01", "Paris", WarehouseKind.Warehouse,
            "5 rue des Entrepots", "Paris", "75012", "FR");
        create = field switch
        {
            "Name" => create with { Name = "Paris\0" },
            "Street" => create with { Street = "Rue\0Paris" },
            "City" => create with { City = "Pa\0ris" },
            _ => create with { PostalCode = "75\000" }
        };
        var update = new UpdateWarehouse(create.Id, create.Name, create.Street, create.City, create.PostalCode, create.CountryCode, true);
        new CreateWarehouseValidator().Validate(create).Errors.Should().ContainSingle(x => x.PropertyName == field);
        new UpdateWarehouseValidator().Validate(update).Errors.Should().ContainSingle(x => x.PropertyName == field);
        FluentActions.Invoking(() => new WarehouseSite(create.Id, create.Code, create.Name, create.Kind,
            create.Street, create.City, create.PostalCode, create.CountryCode)).Should().Throw<ArgumentException>();
        var site = DomainTests.Site();
        FluentActions.Invoking(() => site.Update(update.Name, update.Street, update.City, update.PostalCode,
            update.CountryCode, update.IsActive)).Should().Throw<ArgumentException>();
        site.Name.Should().Be("Paris");
    }

    [Theory]
    [InlineData("operator\0subject")]
    [InlineData("operator\nsubject")]
    [InlineData("\toperator")]
    public void Actor_controls_are_rejected_before_trim_in_validation_and_domain(string actor)
    {
        var command = new ScanParcel(Guid.NewGuid(), Guid.NewGuid(), "PF0123456789ABCDEF01234567", ScanDirection.Entry, actor);
        new ScanParcelValidator().Validate(command).Errors.Should().ContainSingle(x => x.PropertyName == nameof(ScanParcel.OperatorSubject));
        FluentActions.Invoking(() => new WarehouseScan(command.ScanId, Guid.NewGuid(), command.WarehouseId,
            command.TrackingNumber, command.Direction, DateTimeOffset.UtcNow, actor)).Should().Throw<ArgumentException>();
    }
}
