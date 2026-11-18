using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sonara.Data;

namespace Sonara.Api;

/// <summary>
/// Lets <c>dotnet ef</c> create the DbContext directly instead of starting the whole web host. Generating
/// a migration never connects to the database, so no real connection string is needed (set
/// Sonara__Database__ConnectionString to use a specific one, e.g. for <c>dotnet ef database update</c>).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SonaraDbContext>
{
    public SonaraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("Sonara__Database__ConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = "Host=localhost;Database=sonara;Username=sonara;Password=design-time-placeholder";

        var options = new DbContextOptionsBuilder<SonaraDbContext>().UseNpgsql(connectionString).Options;
        return new SonaraDbContext(options);
    }
}
