using Microsoft.AspNetCore.Identity;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Security;

public sealed class UserService(SonaraDbContext db, CredentialProtector protector)
{
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<User> CreateAsync(
        string username, string password, bool isAdmin, CancellationToken ct = default)
    {
        var user = new User
        {
            Username = username,
            IsAdmin = isAdmin,
            SubsonicSecret = protector.Protect(password),
        };
        user.PasswordHash = Hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SetPasswordAsync(User user, string password, CancellationToken ct = default)
    {
        user.SubsonicSecret = protector.Protect(password);
        user.PasswordHash = Hasher.HashPassword(user, password);
        await db.SaveChangesAsync(ct);
    }
}
