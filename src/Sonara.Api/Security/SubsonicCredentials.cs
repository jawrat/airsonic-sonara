using System.Security.Cryptography;
using System.Text;

namespace Sonara.Api.Security;

/// <summary>Verifies the two Subsonic credential styles against the user's secret.</summary>
public static class SubsonicCredentials
{
    /// <param name="secret">The user's plaintext password (decrypted).</param>
    /// <param name="token">"t": hex md5(password + salt).</param>
    /// <param name="salt">"s": random salt chosen by the client.</param>
    /// <param name="password">"p": plain password, or "enc:" + hex-encoded password.</param>
    public static bool Verify(string secret, string? token, string? salt, string? password)
    {
        if (secret.Length == 0) return false;

        if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(salt))
        {
            // MD5 is dictated by the Subsonic protocol, not a choice we get to make.
            var expected = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(secret + salt)));
            return FixedTimeEquals(expected, token.ToLowerInvariant());
        }

        if (!string.IsNullOrEmpty(password))
        {
            string supplied;
            if (password.StartsWith("enc:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    supplied = Encoding.UTF8.GetString(Convert.FromHexString(password[4..]));
                }
                catch (FormatException)
                {
                    return false;
                }
            }
            else
            {
                supplied = password;
            }
            return FixedTimeEquals(supplied, secret);
        }

        return false;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
