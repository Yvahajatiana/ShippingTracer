using System.Text.Json;
using Asp.Versioning;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using ParcelFlow.Customer.Application;

namespace ParcelFlow.Customer.Api;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var api = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(versions).MapToApiVersion(1, 0)
            .RequireAuthorization("customer-or-staff");
        var customers = api.MapGroup("/customers").WithTags("Customers");
        customers.MapPost("", async (ProfileBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateProfile(body.FirstName, body.LastName, body.Email, body.Phone), ct);
            return TypedResults.Created($"/api/v1/customers/{result.Id}", result);
        });
        customers.MapGet("/me", async (ISender sender, CancellationToken ct) => TypedResults.Ok(await sender.Send(new GetMyProfile(), ct)));
        customers.MapGet("", async (ISender sender, CancellationToken ct, int offset = 0, int limit = 20) =>
            TypedResults.Ok(await sender.Send(new ListProfiles(offset, limit), ct))).RequireAuthorization("operations");
        customers.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetProfile(id), ct)));
        customers.MapPut("/{id:guid}", async (Guid id, ProfileBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new UpdateProfile(id, body.FirstName, body.LastName, body.Email, body.Phone), ct)));
        customers.MapPut("/{id:guid}/company", async (Guid id, CompanyAssignmentBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new AssignCompany(id, body.CompanyId), ct))).RequireAuthorization("admin");
        customers.MapPost("/{id:guid}/addresses", async (Guid id, AddressBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddAddress(id, body.Label, body.Line1, body.Line2, body.PostalCode, body.City, body.CountryCode), ct);
            return TypedResults.Created($"/api/v1/customers/{id}/addresses/{result.Id}", result);
        });
        customers.MapPut("/{id:guid}/addresses/{addressId:guid}", async (Guid id, Guid addressId, AddressBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new UpdateAddress(id, addressId, body.Label, body.Line1, body.Line2, body.PostalCode, body.City, body.CountryCode), ct)));
        customers.MapDelete("/{id:guid}/addresses/{addressId:guid}", async (Guid id, Guid addressId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteAddress(id, addressId), ct);
            return TypedResults.NoContent();
        });
        var companies = api.MapGroup("/companies").WithTags("Companies").RequireAuthorization("operations");
        companies.MapGet("", ListCompaniesAsync).Produces<IReadOnlyList<CompanyDto>>();
        companies.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetCompany(id), ct)));
        companies.MapPost("", async (CompanyBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateCompany(body.Name, body.RegistrationNumber, body.Email), ct);
            return TypedResults.Created($"/api/v1/companies/{result.Id}", result);
        }).RequireAuthorization("admin");
        companies.MapPut("/{id:guid}", async (Guid id, CompanyBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new UpdateCompany(id, body.Name, body.RegistrationNumber, body.Email), ct))).RequireAuthorization("admin");
    }

    private static async Task<IResult> ListCompaniesAsync(ISender sender, IDistributedCache cache,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        const string key = "customer:companies:v1:all";
        var logger = loggerFactory.CreateLogger("CompanyCache");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            var cached = await cache.GetStringAsync(key, timeout.Token);
            if (cached is not null && JsonSerializer.Deserialize<CompanyDto[]>(cached) is { } values)
                return TypedResults.Ok(values);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Company cache read unavailable; reading the database");
        }
        var result = await sender.Send(new ListCompanies(), ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
            await cache.SetStringAsync(key, JsonSerializer.Serialize(result),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60) }, timeout.Token);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            logger.LogWarning(exception, "Company cache write unavailable");
        }
        return TypedResults.Ok(result);
    }
}

public sealed record ProfileBody(string FirstName, string LastName, string Email, string? Phone);
public sealed record AddressBody(string Label, string Line1, string? Line2, string PostalCode, string City, string CountryCode);
public sealed record CompanyBody(string Name, string RegistrationNumber, string Email);
public sealed record CompanyAssignmentBody(Guid? CompanyId);
