using Fotbalek.Web.Services.Stats.Core;

namespace Fotbalek.Web.Services.Stats;

public static class StatsServiceCollectionExtensions
{
    /// <summary>Auto-discovers every IStat in this assembly and registers them as singletons alongside the registry and engine.</summary>
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
