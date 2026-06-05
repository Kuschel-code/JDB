using Microsoft.Extensions.DependencyInjection;
using MetaHub.Identification.AniDb;

namespace MetaHub.Identification;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the M3 identification pipeline: the AniDB UDP client (a single shared
    /// session) and the file identification service.
    /// </summary>
    public static IServiceCollection AddIdentification(this IServiceCollection services)
    {
        services.AddOptions<AniDbOptions>()
            .BindConfiguration(AniDbOptions.SectionName);

        // One shared session/socket for AniDB (strict rate limits → serialize through it).
        services.AddSingleton<IAniDbClient, AniDbUdpClient>();
        services.AddScoped<FileIdentificationService>();

        return services;
    }
}
