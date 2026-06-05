using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MetaHub.Infrastructure;

/// <summary>
/// Design-time factory so <c>dotnet ef migrations</c> can construct the context
/// without a running application or live database.
/// </summary>
public class MetaHubDbContextFactory : IDesignTimeDbContextFactory<MetaHubDbContext>
{
    public MetaHubDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("METAHUB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=metahub;Username=metahub;Password=metahub";

        var options = new DbContextOptionsBuilder<MetaHubDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MetaHubDbContext(options);
    }
}
