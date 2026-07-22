using Fotbalek.Application.Features.Stats.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Fotbalek.Application.Features.Stats;

public static class StatsServiceCollectionExtensions
{
    /// <summary>Auto-discovers every IStat in this assembly and registers them as singletons alongside the registry; the engine is scoped (it rides the dispatch scope's DbContext).</summary>
    public static IServiceCollection AddFoosballStats(this IServiceCollection services)
    {
        var statTypes = typeof(IStat).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IStat).IsAssignableFrom(t));

        foreach (var t in statTypes)
        {
            services.AddSingleton(typeof(IStat), t);
        }

        services.AddSingleton<StatRegistry>();
        services.AddScoped<StatsEngine>();
        return services;
    }
}
