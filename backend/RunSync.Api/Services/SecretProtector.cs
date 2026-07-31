// Services/SecretProtector.cs
// AES-256-GCM authenticated encryption for secrets held at rest.
//
// GCM is used rather than CBC because it authenticates as well as encrypts: a tampered
// ciphertext fails loudly on decrypt instead of silently producing garbage plaintext.
//
// Payload format (what actually lands in the database column):
//
//     v1.BASE64( nonce[12] || tag[16] || ciphertext[n] )
//
// The "v1." prefix is a format version, not a key identifier — it lets a future format
// change be detected rather than misparsed. Key rotation is handled separately by trying
// the master key first and then each retired key (see EncryptionConfig.PreviousKeys).
//
// A fresh random nonce is generated for every single encryption. Reusing a nonce under the
// same key is catastrophic for GCM, so nonces are never derived or cached.
//
// The `context` argument is bound in as Additional Authenticated Data (AAD). It is not
// encrypted, but the ciphertext will not decrypt unless the identical context is supplied.
// Callers pass a per-user value, which means a ciphertext lifted from one row and pasted
// onto another user's row is rejected rather than decrypted.
//
// → Interface: ISecretProtector.cs
// → Registered as a singleton in Program.cs (stateless and thread-safe after construction)
// → Consumed by StravaCredentialService.cs

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RunSync.Api.Models.Config;
using RunSync.Api.Services.Interfaces;

namespace RunSync.Api.Services;

public class SecretProtector : ISecretProtector
{
    private const string PayloadPrefix = "v1.";
    private const int KeySizeBytes = 32;   // AES-256
    private const int NonceSizeBytes = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSizeBytes = 16;   // AesGcm.TagByteSizes.MaxSize

    // Index 0 is the master key (used for encryption); the rest are decrypt-only retired keys.
    private readonly byte[][] _keys;

    public SecretProtector(IOptions<EncryptionConfig> options)
    {
        EncryptionConfig config = options.Value;

        byte[] masterKey = ParseKey(config.MasterKey, nameof(EncryptionConfig.MasterKey));

        byte[][] previousKeys = (config.PreviousKeys ?? Array.Empty<string>())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select((k, i) => ParseKey(k, $"{nameof(EncryptionConfig.PreviousKeys)}[{i}]"))
            .ToArray();

        _keys = new[] { masterKey }.Concat(previousKeys).ToArray();
    }

    public string Protect(string plaintext, string context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(context);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] contextBytes = Encoding.UTF8.GetBytes(context);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] tag = new byte[TagSizeBytes];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        using (AesGcm aes = new(_keys[0], TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, contextBytes);
        }

        // Wipe the plaintext copy — the original string stays immutable in memory until GC,
        // but there is no reason to leave a second copy lying around.
        CryptographicOperations.ZeroMemory(plaintextBytes);

        byte[] payload = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSizeBytes + TagSizeBytes, ciphertext.Length);

        return PayloadPrefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string payload, string context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        if (!payload.StartsWith(PayloadPrefix, StringComparison.Ordinal))
            throw new CryptographicException("Encrypted payload is not in a recognised format.");

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(payload[PayloadPrefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted payload is not valid base64.", ex);
        }

        if (raw.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Encrypted payload is truncated.");

        byte[] contextBytes = Encoding.UTF8.GetBytes(context);

        ReadOnlySpan<byte> rawSpan = raw;
        ReadOnlySpan<byte> nonce = rawSpan.Slice(0, NonceSizeBytes);
        ReadOnlySpan<byte> tag = rawSpan.Slice(NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = rawSpan[(NonceSizeBytes + TagSizeBytes)..];

        byte[] plaintextBytes = new byte[ciphertext.Length];

        // Try the master key first, then each retired key. This is what makes rotating
        // Encryption:MasterKey safe: existing rows still decrypt under their original key.
        foreach (byte[] key in _keys)
        {
            try
            {
                using AesGcm aes = new(key, TagSizeBytes);
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes, contextBytes);

                string result = Encoding.UTF8.GetString(plaintextBytes);
                CryptographicOperations.ZeroMemory(plaintextBytes);
                return result;
            }
            catch (CryptographicException)
            {
                // Wrong key, tampered ciphertext, or mismatched context — try the next key.
            }
        }

        CryptographicOperations.ZeroMemory(plaintextBytes);

        // Deliberately vague: the caller must not be able to distinguish "wrong key" from
        // "tampered ciphertext" from "wrong context".
        throw new CryptographicException(
            "Unable to decrypt the stored secret. The encryption key may have changed without " +
            "the previous key being retained in Encryption:PreviousKeys.");
    }

    /// <summary>
    /// Decodes and validates a configured base64 key, failing with an actionable message.
    /// Called from the constructor so a bad key surfaces at startup, not at first use.
    /// </summary>
    private static byte[] ParseKey(string base64Key, string settingName)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException(
                $"Encryption:{settingName} is not configured. Generate one with: openssl rand -base64 32");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Encryption:{settingName} is not valid base64. Generate one with: openssl rand -base64 32", ex);
        }

        if (key.Length != KeySizeBytes)
            throw new InvalidOperationException(
                $"Encryption:{settingName} must decode to exactly {KeySizeBytes} bytes (AES-256), " +
                $"but decoded to {key.Length}. Generate one with: openssl rand -base64 32");

        return key;
    }
}

/*
 * ─── WHAT CONNECTS HERE ───────────────────────────────────────────────────────
 * This file is called by:   StravaCredentialService.cs (protects Strava client secrets)
 * This file calls into:     System.Security.Cryptography (AesGcm)
 *                           EncryptionConfig.cs (master + retired keys)
 * Next logical file to read: StravaCredentialService.cs
 * ─────────────────────────────────────────────────────────────────────────────
 */
