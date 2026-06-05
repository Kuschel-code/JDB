using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MetaHub.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the MetaHub <see cref="MetaHubDbContext"/> against PostgreSQL.
    /// </summary>
    public static IServiceCollection AddMetaHubInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<MetaHubDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(MetaHubDbContext).Assembly.FullName)));

        return services;
    }
}
