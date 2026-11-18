using System.Text.RegularExpressions;

namespace Sonara.Api.Security;

/// <summary>Pure validation rules for user management.</summary>
public static partial class UserRules
{
    public const int MinPasswordLength = 8;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@-]{0,63}$")]
    private static partial Regex UsernamePattern();

    public static bool IsValidUsername(string? username) =>
        !string.IsNullOrEmpty(username) && UsernamePattern().IsMatch(username);

    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password) && password.Length >= MinPasswordLength;

    /// <summary>Subsonic clients may send passwords as "enc:" followed by the hex of the UTF-8 bytes.</summary>
    public static string? DecodePassword(string? raw)
    {
        if (raw is null) return null;
        if (!raw.StartsWith("enc:", StringComparison.Ordinal)) return raw;
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromHexString(raw[4..]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// True if removing admin rights from (or deleting) <paramref name="target"/> would leave the
    /// server without any administrator.
    /// </summary>
    public static bool WouldRemoveLastAdmin(IEnumerable<(int Id, bool IsAdmin)> users, int targetId) =>
        !users.Any(u => u.IsAdmin && u.Id != targetId);
}
