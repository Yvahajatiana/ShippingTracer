using FluentAssertions;
using ParcelFlow.Customer.Application;
using Xunit;

namespace ParcelFlow.Customer.UnitTests;

public sealed class ValidationTests
{
    [Fact]
    public async Task Create_profile_reports_all_invalid_fields()
    {
        var result = await new CreateProfileValidator().ValidateAsync(new CreateProfile("", "", "invalid", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Distinct().Should().BeEquivalentTo("FirstName", "LastName", "Email", "Phone");
    }

    [Fact]
    public async Task Optional_phone_and_international_address_are_supported()
    {
        (await new CreateProfileValidator().ValidateAsync(new CreateProfile("Marie", "Martin", "marie@example.test", null)))
            .IsValid.Should().BeTrue();
        (await new AddAddressValidator().ValidateAsync(new AddAddress(Guid.NewGuid(), "Bureau", "12 avenue de France", null,
            "101", "Antananarivo", "MG"))).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    public async Task Pagination_rejects_unbounded_or_negative_requests(int offset, int limit)
    {
        var result = await new ListProfilesValidator().ValidateAsync(new ListProfiles(offset, limit));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Address_requires_valid_owner_and_iso_country()
    {
        var result = await new AddAddressValidator().ValidateAsync(new AddAddress(Guid.Empty, "Maison", "10 rue de la Paix", null,
            "75002", "Paris", "ZZ"));
        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo("CustomerId", "CountryCode");
    }

    [Fact]
    public async Task Clearing_company_is_valid_but_empty_identifier_is_not()
    {
        var validator = new AssignCompanyValidator();
        (await validator.ValidateAsync(new AssignCompany(Guid.NewGuid(), null))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(new AssignCompany(Guid.NewGuid(), Guid.Empty))).IsValid.Should().BeFalse();
    }
}
