using Microsoft.Extensions.DependencyInjection;

namespace MetaHub.Export;

public static class DependencyInjection
{
    /// <summary>Registers the NFO export service (M5).</summary>
    public static IServiceCollection AddNfoExport(this IServiceCollection services)
    {
        services.AddScoped<NfoExportService>();
        return services;
    }
}
