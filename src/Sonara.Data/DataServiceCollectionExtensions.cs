using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sonara.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Registers the DbContext against PostgreSQL.</summary>
    public static IServiceCollection AddSonaraData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SonaraDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
