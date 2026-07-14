using TaskNotify.Infrastructure.Email;
using Xunit;

namespace TaskNotify.Core.Tests;

/// <summary>
/// DPAPI round-trip tests. Only run on Windows — <c>System.Security.Cryptography.ProtectedData</c>
/// throws <c>PlatformNotSupportedException</c> elsewhere, which the protector swallows
/// into an empty string. Tests here assume a Windows host (matches the app's deployment).
/// </summary>
public class EmailPasswordProtectorTests
{
    [Fact]
    public void EncryptThenDecryptRoundTrips()
    {
        var original = "my-smtp-password-123!@#";
        var encrypted = EmailPasswordProtector.Encrypt(original);

        Assert.NotEmpty(encrypted);
        Assert.NotEqual(original, encrypted);

        var decrypted = EmailPasswordProtector.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EncryptReturnsEmptyForNullOrBlank(string? input)
    {
        // ReSharper disable once AssignNullToNotNullAttribute — testing the null path is the point
        var result = EmailPasswordProtector.Encrypt(input!);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DecryptReturnsEmptyForNullOrBlank(string? input)
    {
        // ReSharper disable once AssignNullToNotNullAttribute — testing the null path is the point
        var result = EmailPasswordProtector.Decrypt(input!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DecryptReturnsEmptyForCorruptBase64()
    {
        var result = EmailPasswordProtector.Decrypt("not!!valid!!base64@@");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DecryptReturnsEmptyForValidBase64ButWrongBytes()
    {
        // Valid base64 of arbitrary bytes — DPAPI will reject as not its ciphertext.
        var junk = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });
        var result = EmailPasswordProtector.Decrypt(junk);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EncryptWithDifferentInvocationsProducesDifferentCiphertext()
    {
        // DPAPI uses a random IV, so encrypting the same plaintext twice should
        // produce different ciphertexts (both decryptable).
        var a = EmailPasswordProtector.Encrypt("same-password");
        var b = EmailPasswordProtector.Encrypt("same-password");
        Assert.NotEqual(a, b);
        Assert.Equal("same-password", EmailPasswordProtector.Decrypt(a));
        Assert.Equal("same-password", EmailPasswordProtector.Decrypt(b));
    }
}
