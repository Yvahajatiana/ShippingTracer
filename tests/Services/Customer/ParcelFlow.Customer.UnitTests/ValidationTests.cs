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

    [Fact]
    public async Task Emails_surrounded_by_whitespace_are_accepted_because_they_are_trimmed()
    {
        (await new CreateProfileValidator().ValidateAsync(new CreateProfile("Marie", "Martin", "  marie@example.test \t", null)))
            .IsValid.Should().BeTrue();
        (await new UpdateProfileValidator().ValidateAsync(new UpdateProfile(Guid.NewGuid(), "Marie", "Martin", " marie@example.test ", null)))
            .IsValid.Should().BeTrue();
        (await new CreateCompanyValidator().ValidateAsync(new CreateCompany("Acme", "ACME-1", " billing@acme.test ")))
            .IsValid.Should().BeTrue();
        (await new UpdateCompanyValidator().ValidateAsync(new UpdateCompany(Guid.NewGuid(), "Acme", "ACME-1", " billing@acme.test ")))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("marie@")]
    [InlineData("Marie <marie@example.test>")]
    [InlineData("marie @example.test")]
    public async Task Invalid_emails_are_rejected_after_trimming(string email)
    {
        var result = await new CreateProfileValidator().ValidateAsync(new CreateProfile("Marie", "Martin", email, null));

        result.Errors.Select(error => error.PropertyName).Distinct().Should().Equal("Email");
    }

    [Fact]
    public async Task Email_length_is_checked_on_the_trimmed_value()
    {
        var email = new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', 56) + ".test";
        email.Length.Should().Be(254);
        var validator = new CreateProfileValidator();

        (await validator.ValidateAsync(new CreateProfile("Marie", "Martin", $"  {email}  ", null))).IsValid.Should().BeTrue();
        (await validator.ValidateAsync(new CreateProfile("Marie", "Martin", "a" + email, null))).IsValid.Should().BeFalse();
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
