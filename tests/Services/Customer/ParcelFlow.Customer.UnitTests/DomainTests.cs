using FluentAssertions;
using ParcelFlow.Customer.Domain;
using Xunit;

namespace ParcelFlow.Customer.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void Invalid_profile_update_preserves_existing_values()
    {
        var profile = NewCustomer();
        var update = () => profile.Update("Changed", "Changed", "invalid-email", null);

        update.Should().Throw<ArgumentException>();
        profile.FirstName.Should().Be("Marie");
        profile.LastName.Should().Be("Martin");
        profile.Email.Should().Be("marie@example.test");
    }

    [Fact]
    public void Address_is_owned_by_its_customer_and_cannot_be_removed_by_another()
    {
        var owner = NewCustomer();
        var other = NewCustomer();
        var address = owner.AddAddress("Domicile", "10 rue de la Paix", null, "75002", "Paris", "FR");

        address.CustomerId.Should().Be(owner.Id);
        var removeByOther = () => other.RemoveAddress(address);
        removeByOther.Should().Throw<ArgumentException>();
        owner.Addresses.Should().ContainSingle().Which.Should().BeSameAs(address);
        owner.RemoveAddress(address);
        owner.Addresses.Should().BeEmpty();
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("ZZ")]
    [InlineData("FRA")]
    [InlineData("")]
    public void Address_rejects_invalid_country_code(string countryCode)
    {
        var customer = NewCustomer();
        var add = () => customer.AddAddress("Domicile", "10 rue de la Paix", null, "75002", "Paris", countryCode);

        add.Should().Throw<ArgumentException>();
        customer.Addresses.Should().BeEmpty();
    }

    [Fact]
    public void Company_assignment_can_be_cleared_but_not_set_to_empty_identifier()
    {
        var customer = NewCustomer();
        var companyId = Guid.NewGuid();
        customer.AssignCompany(companyId);
        var assignEmpty = () => customer.AssignCompany(Guid.Empty);
        assignEmpty.Should().Throw<ArgumentException>();
        customer.CompanyId.Should().Be(companyId);
        customer.AssignCompany(null);
        customer.CompanyId.Should().BeNull();
    }

    [Fact]
    public void Profile_has_server_generated_identity_and_normalized_names()
    {
        var customer = new CustomerProfile("subject", " Marie ", " Martin ", "marie@example.test", null);
        customer.Id.Should().NotBeEmpty();
        customer.FirstName.Should().Be("Marie");
        customer.LastName.Should().Be("Martin");
        customer.CreatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    private static CustomerProfile NewCustomer() => new(Guid.NewGuid().ToString(), "Marie", "Martin", "marie@example.test", null);
}
