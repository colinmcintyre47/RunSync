// Models/Config/EncryptionConfig.cs
// Configuration for the envelope encryption used to protect per-user Strava client secrets
// at rest. Bound from the "Encryption" section of appsettings.json.
//
// MasterKey is the ONLY key used to encrypt new values. PreviousKeys exist purely so that
// rotating MasterKey doesn't strand ciphertext that was written under an older key —
// SecretProtector tries the master key first, then each previous key in order.
//
// Key format: base64-encoded 32 bytes (256 bits). Generate one with:
//   openssl rand -base64 32
//
// These values are secrets. Local dev keeps them in appsettings.Development.json (git-ignored);
// production supplies them via AWS Secrets Manager → Elastic Beanstalk environment properties
// as Encryption__MasterKey / Encryption__PreviousKeys__0.
//
// → Consumed by SecretProtector.cs
// → Validated at startup in Program.cs so a misconfigured key fails fast rather than at first use

namespace RunSync.Api.Models.Config;

public class EncryptionConfig
{
    /// <summary>Base64-encoded 256-bit key. Used to encrypt and decrypt.</summary>
    public string MasterKey { get; set; } = string.Empty;

    /// <summary>
    /// Base64-encoded 256-bit keys retired by rotation. Decrypt-only — never used for new writes.
    /// Keep a retired key here until every stored secret has been re-encrypted under the new
    /// master key, then remove it.
    /// </summary>
    public string[] PreviousKeys { get; set; } = Array.Empty<string>();
}
