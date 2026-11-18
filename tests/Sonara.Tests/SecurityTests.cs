using System.Security.Cryptography;
using System.Text;
using Sonara.Api.Security;

namespace Sonara.Tests;

public class CredentialProtectorTests
{
    private static CredentialProtector NewProtector() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Round_trips()
    {
        var p = NewProtector();
        Assert.Equal("s3cret ✓", p.Unprotect(p.Protect("s3cret ✓")));
    }

    [Fact]
    public void Same_plaintext_encrypts_differently_each_time()
    {
        var p = NewProtector();
        Assert.NotEqual(p.Protect("x"), p.Protect("x"));
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var p = NewProtector();
        var bytes = Convert.FromBase64String(p.Protect("hello"));
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(bytes)));
    }

    [Fact]
    public void A_different_key_cannot_decrypt()
    {
        var protectedValue = NewProtector().Protect("hello");
        Assert.ThrowsAny<CryptographicException>(() => NewProtector().Unprotect(protectedValue));
    }
}

public class SubsonicCredentialsTests
{
    private static string Token(string password, string salt) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password + salt)));

    [Fact]
    public void Token_and_salt_authenticate()
    {
        Assert.True(SubsonicCredentials.Verify("sesame", Token("sesame", "c19b2d"), "c19b2d", null));
    }

    [Fact]
    public void Matches_the_example_in_the_Subsonic_API_docs()
    {
        Assert.True(SubsonicCredentials.Verify("sesame", "26719a1196d2a940705a59634eb18eab", "c19b2d", null));
    }

    [Fact]
    public void Token_is_case_insensitive_hex()
    {
        Assert.True(SubsonicCredentials.Verify("sesame", Token("sesame", "abc").ToUpperInvariant(), "abc", null));
    }

    [Fact]
    public void Wrong_token_is_rejected()
    {
        Assert.False(SubsonicCredentials.Verify("sesame", Token("other", "abc"), "abc", null));
    }

    [Fact]
    public void Plain_and_hex_encoded_passwords_authenticate()
    {
        var hex = Convert.ToHexStringLower(Encoding.UTF8.GetBytes("sesame"));
        Assert.True(SubsonicCredentials.Verify("sesame", null, null, "sesame"));
        Assert.True(SubsonicCredentials.Verify("sesame", null, null, "enc:" + hex));
        Assert.False(SubsonicCredentials.Verify("sesame", null, null, "enc:zz"));
        Assert.False(SubsonicCredentials.Verify("sesame", null, null, "nope"));
    }

    [Fact]
    public void Empty_secret_never_authenticates()
    {
        Assert.False(SubsonicCredentials.Verify("", Token("", "abc"), "abc", null));
        Assert.False(SubsonicCredentials.Verify("", null, null, ""));
    }
}
