using System;
using System.Security.Cryptography;

namespace EmployeeMonitoring.Common.Security;

/// <summary>
/// AES-256-GCM encryption for data at rest and in transit.
/// Uses authenticated encryption to prevent tampering.
/// </summary>
public static class AesGcmEncryption
{
    private const int KeySize = 32; // 256 bits
    private const int NonceSize = 12; // 96 bits (recommended for GCM)
    private const int TagSize = 16; // 128 bits

    /// <summary>
    /// Encrypts data using AES-256-GCM with a random nonce.
    /// Returns: nonce (12 bytes) + ciphertext + tag (16 bytes)
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[]? associatedData = null)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256 bits)", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        // Combine: nonce + ciphertext + tag
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return result;
    }

    /// <summary>
    /// Decrypts data encrypted with Encrypt().
    /// Expects: nonce (12 bytes) + ciphertext + tag (16 bytes)
    /// </summary>
    public static byte[] Decrypt(byte[] encryptedData, byte[] key, byte[]? associatedData = null)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be {KeySize} bytes (256 bits)", nameof(key));

        if (encryptedData.Length < NonceSize + TagSize)
            throw new ArgumentException("Encrypted data too short", nameof(encryptedData));

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = encryptedData.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encryptedData, NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(encryptedData, NonceSize + ciphertextLength, tag, 0, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    /// <summary>
    /// Derives a 256-bit key from a password using PBKDF2.
    /// </summary>
    public static byte[] DeriveKey(string password, byte[] salt, int iterations = 100_000)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeySize);
    }

    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    public static byte[] GenerateSalt(int size = 32)
    {
        return RandomNumberGenerator.GetBytes(size);
    }
}