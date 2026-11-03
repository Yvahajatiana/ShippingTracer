using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ParcelFlow.BuildingBlocks.Application.Correlation;
using ParcelFlow.BuildingBlocks.Application.Validation;

namespace ParcelFlow.Customer.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCustomerApplication(this IServiceCollection services)
    {
        services.AddParcelFlowCorrelation();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddScoped<CustomerAccess>();
        services.AddScoped<IValidator<CreateProfile>, CreateProfileValidator>();
        services.AddScoped<IValidator<UpdateProfile>, UpdateProfileValidator>();
        services.AddScoped<IValidator<GetProfile>, GetProfileValidator>();
        services.AddScoped<IValidator<ListProfiles>, ListProfilesValidator>();
        services.AddScoped<IValidator<AddAddress>, AddAddressValidator>();
        services.AddScoped<IValidator<UpdateAddress>, UpdateAddressValidator>();
        services.AddScoped<IValidator<DeleteAddress>, DeleteAddressValidator>();
        services.AddScoped<IValidator<AssignCompany>, AssignCompanyValidator>();
        services.AddScoped<IValidator<CreateCompany>, CreateCompanyValidator>();
        services.AddScoped<IValidator<UpdateCompany>, UpdateCompanyValidator>();
        services.AddScoped<IValidator<GetCompany>, GetCompanyValidator>();
        return services;
    }
}
