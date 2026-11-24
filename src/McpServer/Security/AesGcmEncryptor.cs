using System.Security.Cryptography;
using System.Text;

namespace McpServer.Security;

/// <summary>
/// Provides authenticated encryption and decryption using AES-256-GCM.
/// AES-GCM guarantees both confidentiality (encryption) and authenticity/integrity (authentication tag).
/// Output wire format: Base64([12-byte Nonce] + [16-byte Tag] + [Ciphertext]).
/// </summary>
public sealed class AesGcmEncryptor : IDisposable
{
    public const int NonceSizeBytes = 12; // 96 bits - standard recommended nonce size for AES-GCM
    public const int TagSizeBytes = 16;   // 128 bits - maximum tag size for integrity verification
    private const int KeySizeBytes = 32;  // 256 bits

    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance with a 256-bit (32-byte) key represented as a 64-character hex string.
    /// </summary>
    /// <param name="hexKey">64-character hexadecimal string representing the 32-byte key.</param>
    public AesGcmEncryptor(string hexKey)
    {
        if (string.IsNullOrWhiteSpace(hexKey))
            throw new ArgumentException("Encryption key cannot be null or empty.", nameof(hexKey));

        try
        {
            _key = Convert.FromHexString(hexKey.Trim());
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Encryption key must be a valid hex string.", nameof(hexKey), ex);
        }

        if (_key.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"Encryption key must be exactly {KeySizeBytes} bytes ({KeySizeBytes * 2} hex chars). Provided key was {_key.Length} bytes.",
                nameof(hexKey));
        }
    }

    /// <summary>
    /// Encrypts plaintext string using AES-256-GCM with a newly generated random 12-byte nonce.
    /// </summary>
    /// <param name="plaintext">The text to encrypt.</param>
    /// <returns>A Base64-encoded string combining nonce, tag, and ciphertext.</returns>
    public string Encrypt(string plaintext)
    {
        if (plaintext is null)
            throw new ArgumentNullException(nameof(plaintext));

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        // Generate a cryptographically random 12-byte nonce for each operation
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine into a single byte array: [Nonce (12)] + [Tag (16)] + [Ciphertext (N)]
        var combined = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, combined, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        return Convert.ToBase64String(combined);
    }

    /// <summary>
    /// Decrypts a Base64-encoded AES-256-GCM payload.
    /// Throws CryptographicException if the tag does not match or data has been tampered with.
    /// </summary>
    /// <param name="encryptedBase64">The Base64-encoded encrypted blob.</param>
    /// <returns>The decrypted plaintext string.</returns>
    public string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64))
            throw new ArgumentException("Encrypted payload cannot be null or empty.", nameof(encryptedBase64));

        byte[] combined;
        try
        {
            combined = Convert.FromBase64String(encryptedBase64);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid Base64 payload format.", ex);
        }

        if (combined.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Encrypted data is corrupted or truncated (shorter than nonce + tag).");
        }

        var nonce = combined[..NonceSizeBytes];
        var tag = combined[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = combined[(NonceSizeBytes + TagSizeBytes)..];

        var plaintextBytes = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        // AesGcm.Decrypt verifies the authentication tag. If tampered with, throws CryptographicException.
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
    }
}
