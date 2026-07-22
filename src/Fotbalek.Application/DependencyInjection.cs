using System.Reflection;
using FluentValidation;
using Fotbalek.Application.Common;
using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Application.Common.Authorization;
using Fotbalek.Application.Common.Behaviors;
using Fotbalek.Application.Common.Events;
using Fotbalek.Application.Features.Stats;
using Microsoft.Extensions.DependencyInjection;

namespace Fotbalek.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the mediator, the behavior pipeline (Logging → Validation → Transaction),
    /// validators, and the Application services. Pass the host assembly in
    /// <paramref name="additionalAssemblies"/> so the host's bridge INotificationHandlers
    /// (chat → ChatNotifier) get discovered too (AI/architecture.md §4.2).
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        params Assembly[] additionalAssemblies)
    {
        var applicationAssembly = typeof(DependencyInjection).Assembly;
        Assembly[] assemblies = [applicationAssembly, .. additionalAssemblies];

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assemblies);
            // Order matters: Logging → Validation → Transaction.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);

        services.AddScoped<UserContext>();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<UserContext>());
        services.AddScoped<IEventCollector, EventCollector>();
        services.AddScoped<TeamAccess>();

        services.AddFoosballStats();

        return services;
    }
}
