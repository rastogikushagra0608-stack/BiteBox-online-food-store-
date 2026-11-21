using BiteBox.Application.Abstractions;
using BiteBox.Application.Features.AI;
using BiteBox.Infrastructure.AI;
using BiteBox.Infrastructure.Identity;
using BiteBox.Infrastructure.Persistence;
using BiteBox.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BiteBox.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BiteBox")
            ?? throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:BiteBox' is missing. Set it with `dotnet user-secrets` or the ConnectionStrings__BiteBox environment variable.");

        services.AddDbContext<BiteBoxDbContext>(options => options
            .UseMySQL(connectionString, mysql =>
            {
                mysql.EnableRetryOnFailure(3);
                // Orders load Items + History + Payment; split queries avoid a cartesian explosion.
                mysql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            }));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<BiteBoxDbContext>());

        services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<BiteBoxDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPaymentGateway, SimulatedPaymentGateway>();

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.Section));
        services.PostConfigure<AiOptions>(o => o.Claude.ApiKey ??= configuration["ANTHROPIC_API_KEY"]);
        services.AddSingleton<IFoodIntentExtractor, ClaudeFoodIntentExtractor>();

        services.AddScoped<DatabaseSeeder>();
        return services;
    }
}
