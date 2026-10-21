using Asp.Versioning;
using MediatR;
using ParcelFlow.Reporting.Application;

namespace ParcelFlow.Reporting.Api;

public static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var reports = app.MapGroup("/api/v{version:apiVersion}/reports")
            .WithApiVersionSet(versions).MapToApiVersion(1, 0).WithTags("Reports")
            .RequireAuthorization("reporting-users");
        reports.MapGet("/kpis", async (DateOnly from, DateOnly to, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetKpis(from, to), ct)));
    }
}
