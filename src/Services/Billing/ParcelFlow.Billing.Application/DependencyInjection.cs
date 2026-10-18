using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Billing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IValidator<RegisterInvoice>, RegisterInvoiceValidator>();
        services.AddScoped<IValidator<PayInvoice>, PayInvoiceValidator>();
        services.AddScoped<IValidator<IssueCreditNote>, IssueCreditNoteValidator>();
        services.AddScoped<IValidator<GetInvoice>, GetInvoiceValidator>();
        services.AddScoped<IValidator<GetInvoicePayment>, GetInvoicePaymentValidator>();
        services.AddScoped<IValidator<GetCreditNote>, GetCreditNoteValidator>();
        services.AddScoped<IValidator<ListInvoices>, ListInvoicesValidator>();
        services.AddScoped<IValidator<ListInvoiceCreditNotes>, ListInvoiceCreditNotesValidator>();
        return services;
    }
}
