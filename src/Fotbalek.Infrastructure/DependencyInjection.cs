using Fotbalek.Application.Common.Abstractions;
using Fotbalek.Domain.Entities;
using Fotbalek.Infrastructure.Identity;
using Fotbalek.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fotbalek.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        // Plain scoped registration — one context per dispatch/request scope (the old
        // IDbContextFactory pattern is gone; IScopedSender guarantees short-lived scopes).
        // ⚠ Keep plain UseSqlServer: EnableRetryOnFailure is incompatible with the
        // TransactionBehavior's user-initiated transactions (AI/architecture.md §4.2).
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IDbLocks, SqlServerDbLocks>();
        services.AddSingleton<ITeamPasswordHasher, IdentityTeamPasswordHasher>();

        // Identity core + stores. SignInManager AND AddDefaultTokenProviders cannot be registered
        // here — both extensions live in the ASP.NET shared framework, which a plain class library
        // doesn't reference (§4.6; the spec listed token providers here, but they hit the exact
        // constraint it documents for SignInManager). Web layers both on via a fresh
        // IdentityBuilder; the default token providers are needed by admin password reset.
        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 6;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireDigit = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.SignIn.RequireConfirmedAccount = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<int>>()
            .AddEntityFrameworkStores<AppDbContext>();

        return services;
    }
}
