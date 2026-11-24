using System.Security.Cryptography;
using McpServer.Security;
using Xunit;

namespace McpServer.UnitTests.Security;

public sealed class AesGcmEncryptorTests
{
    private const string Valid32ByteHexKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Encrypt_Decrypt_RoundTrip_ReturnsOriginalPlaintext()
    {
        // Arrange
        using var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        const string originalText = "my-super-secret-oauth-bearer-token-1234567890!@#$%^&*()";

        // Act
        var encrypted = encryptor.Encrypt(originalText);
        var decrypted = encryptor.Decrypt(encrypted);

        // Assert
        Assert.NotNull(encrypted);
        Assert.NotEqual(originalText, encrypted);
        Assert.Equal(originalText, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesUniqueCiphertexts_ForSamePlaintext()
    {
        // Arrange
        using var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        const string text = "identical-token";

        // Act
        var ciphertext1 = encryptor.Encrypt(text);
        var ciphertext2 = encryptor.Encrypt(text);

        // Assert - Random 12-byte nonces ensure ciphertext uniqueness
        Assert.NotEqual(ciphertext1, ciphertext2);

        // Both still decrypt to the exact same plaintext
        Assert.Equal(text, encryptor.Decrypt(ciphertext1));
        Assert.Equal(text, encryptor.Decrypt(ciphertext2));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        // Arrange
        using var encryptor1 = new AesGcmEncryptor(Valid32ByteHexKey);
        const string differentKey = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        using var encryptor2 = new AesGcmEncryptor(differentKey);

        var encrypted = encryptor1.Encrypt("confidential-token-content");

        // Act & Assert (.NET 8/10 introduces AuthenticationTagMismatchException which inherits CryptographicException)
        Assert.ThrowsAny<CryptographicException>(() => encryptor2.Decrypt(encrypted));
    }

    [Fact]
    public void Decrypt_WithTamperedData_ThrowsCryptographicException()
    {
        // Arrange
        using var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        var encrypted = encryptor.Encrypt("uncompromised-token");

        // Tamper with a byte in the Base64 payload
        var rawBytes = Convert.FromBase64String(encrypted);
        rawBytes[^1] ^= 0xFF; // Flip bits in the ciphertext / tag
        var tamperedBase64 = Convert.ToBase64String(rawBytes);

        // Act & Assert - AES-GCM authentication tag verification must reject tampered data
        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(tamperedBase64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789abcdef")] // 8 bytes (16 chars) - too short
    [InlineData("not-a-valid-hex-string-at-all-because-it-has-invalid-chars-xyz!")]
    public void Constructor_WithInvalidKey_ThrowsArgumentException(string invalidKey)
    {
        Assert.Throws<ArgumentException>(() => new AesGcmEncryptor(invalidKey));
    }
}
