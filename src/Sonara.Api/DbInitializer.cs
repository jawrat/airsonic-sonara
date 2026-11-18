using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Api.Security;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api;

public static class DbInitializer
{
    private const string PasswordAlphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<SonaraDbContext>();
        var options = sp.GetRequiredService<IOptions<SonaraOptions>>().Value;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var password = options.AdminPassword;
            var generated = string.IsNullOrEmpty(password);
            if (generated) password = RandomNumberGenerator.GetString(PasswordAlphabet, 16);

            await sp.GetRequiredService<UserService>().CreateAsync(options.AdminUser, password!, isAdmin: true);

            if (generated)
            {
                logger.LogWarning(
                    "Created initial admin user '{User}' with generated password: {Password}  (shown once; change it)",
                    options.AdminUser, password);
            }
            else
            {
                logger.LogInformation("Created initial admin user '{User}'", options.AdminUser);
            }
        }

        if (!await db.MusicFolders.AnyAsync() && !string.IsNullOrWhiteSpace(options.MusicFolder))
        {
            db.MusicFolders.Add(new MusicFolder { Name = "Music", Path = Path.GetFullPath(options.MusicFolder) });
            await db.SaveChangesAsync();
            logger.LogInformation("Registered music folder {Path}", options.MusicFolder);
        }
    }
}
