using Sonara.Api.Security;

namespace Sonara.Tests;

public class UserRulesTests
{
    [Theory]
    [InlineData("admin", true)]
    [InlineData("a.b-c_d@x", true)]
    [InlineData("", false)]
    [InlineData(" bob", false)]
    [InlineData("-bob", false)]
    [InlineData("bob smith", false)]
    [InlineData("bob/../x", false)]
    public void Username_validation(string name, bool expected) =>
        Assert.Equal(expected, UserRules.IsValidUsername(name));

    [Fact]
    public void Username_longer_than_64_is_rejected() =>
        Assert.False(UserRules.IsValidUsername(new string('a', 65)));

    [Theory]
    [InlineData("1234567", false)]
    [InlineData("12345678", true)]
    [InlineData(null, false)]
    public void Password_validation(string? pw, bool expected) =>
        Assert.Equal(expected, UserRules.IsValidPassword(pw));

    [Fact]
    public void Hex_encoded_passwords_are_decoded()
    {
        Assert.Equal("sesame", UserRules.DecodePassword("enc:736573616d65"));
        Assert.Equal("plain", UserRules.DecodePassword("plain"));
        Assert.Null(UserRules.DecodePassword("enc:zz"));
    }

    [Fact]
    public void Last_admin_is_protected()
    {
        (int, bool)[] users = [(1, true), (2, false)];
        Assert.True(UserRules.WouldRemoveLastAdmin(users, 1));
        Assert.False(UserRules.WouldRemoveLastAdmin(users, 2));
        Assert.False(UserRules.WouldRemoveLastAdmin([(1, true), (3, true)], 1));
    }
}
