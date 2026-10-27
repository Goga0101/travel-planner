using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;

namespace TravelPlanner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        services.AddSingleton<IOutboundLinkPolicy, OutboundLinkPolicy>();
        services.AddScoped<TripPlanningEngine>();
        services.AddScoped<TripSearchProcessor>();
        services.AddScoped<Features.TripSearches.CityDestinationResolver>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
