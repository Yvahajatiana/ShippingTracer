using System.Text.Json.Serialization;
using Asp.Versioning;
using MediatR;
using ParcelFlow.Billing.Application;
using ParcelFlow.Billing.Domain;

namespace ParcelFlow.Billing.Api;

public sealed class HttpBillingActor(IHttpContextAccessor accessor) : IBillingActor
{
    public string Subject => accessor.HttpContext?.User.FindFirst("sub")?.Value ?? string.Empty;
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("admin") == true;
}

public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this WebApplication app)
    {
        var versions = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        var billing = app.MapGroup("/api/v{version:apiVersion}/billing")
            .WithApiVersionSet(versions).MapToApiVersion(1, 0).WithTags("Billing")
            .RequireAuthorization("billing-users");
        billing.MapGet("/invoices", async (ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListInvoices(page, pageSize), ct)));
        billing.MapGet("/invoices/{invoiceId:guid}", async (Guid invoiceId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetInvoice(invoiceId), ct)));
        billing.MapGet("/invoices/{invoiceId:guid}/payment", async (Guid invoiceId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetInvoicePayment(invoiceId), ct)));
        billing.MapPost("/invoices/{invoiceId:guid}/payments", async (Guid invoiceId, PayInvoiceBody body, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new PayInvoice(invoiceId, body.OperationId, body.Outcome), ct)));
        billing.MapGet("/invoices/{invoiceId:guid}/credit-notes", async (Guid invoiceId, ISender sender, CancellationToken ct, int page = 1, int pageSize = 50) =>
            TypedResults.Ok(await sender.Send(new ListInvoiceCreditNotes(invoiceId, page, pageSize), ct)));
        billing.MapGet("/invoices/{invoiceId:guid}/credit-notes/{creditNoteId:guid}", async (Guid invoiceId, Guid creditNoteId, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetCreditNote(invoiceId, creditNoteId), ct)));
        billing.MapPost("/invoices/{invoiceId:guid}/credit-notes", async (Guid invoiceId, IssueCreditNoteBody body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new IssueCreditNote(invoiceId, body.OperationId, body.Amount, body.Reason), ct);
            return TypedResults.Created($"/api/v1/billing/invoices/{invoiceId}/credit-notes/{result.Id}", result);
        }).RequireAuthorization("admin");
    }
}

public sealed record PayInvoiceBody([property: JsonRequired] Guid OperationId, [property: JsonRequired] MockPaymentOutcome Outcome);
public sealed record IssueCreditNoteBody([property: JsonRequired] Guid OperationId, [property: JsonRequired] decimal Amount, [property: JsonRequired] CreditReason Reason);
