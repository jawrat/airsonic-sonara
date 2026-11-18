using System.Security.Cryptography;
using System.Text;

namespace Sonara.Api.Security;

/// <summary>
/// AES-256-GCM encryption for credentials that must be retrievable (the Subsonic
/// token scheme needs the plaintext password server-side).
/// Stored format: base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public sealed class CredentialProtector(byte[] key)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string Protect(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        cipher.CopyTo(output, NonceSize + TagSize);
        return Convert.ToBase64String(output);
    }

    /// <exception cref="CryptographicException">Wrong key, or the value was tampered with.</exception>
    public string Unprotect(string protectedValue)
    {
        var input = Convert.FromBase64String(protectedValue);
        if (input.Length < NonceSize + TagSize)
            throw new CryptographicException("Protected value is too short.");

        var nonce = input.AsSpan(0, NonceSize);
        var tag = input.AsSpan(NonceSize, TagSize);
        var cipher = input.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>Reads the 32-byte key from <paramref name="path"/>, creating it on first run.</summary>
    public static byte[] LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
            return Convert.FromBase64String(File.ReadAllText(path).Trim());

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(path, Convert.ToBase64String(key));
        // On Windows, rely on the data directory's ACLs.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return key;
    }
}
