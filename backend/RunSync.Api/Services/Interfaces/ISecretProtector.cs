// Services/Interfaces/ISecretProtector.cs
// Contract for authenticated symmetric encryption of secrets stored in the database.
//
// Deliberately NOT ASP.NET Core Data Protection: its default key ring lives on the local
// file system, which Elastic Beanstalk wipes on redeploy and does not share between
// instances — every stored secret would become permanently undecryptable after a deploy.
// A configuration-supplied master key survives redeploys and works across instances.
//
// → Implemented by SecretProtector.cs (AES-256-GCM)
// → Used by StravaCredentialService.cs to protect per-user Strava client secrets

namespace RunSync.Api.Services.Interfaces;

public interface ISecretProtector
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns a self-describing, versioned payload
    /// safe to persist as text.
    /// </summary>
    /// <param name="context">
    /// Additional authenticated data bound into the ciphertext. The exact same value must be
    /// supplied to <see cref="Unprotect"/>. Pass something that identifies the row (e.g. the
    /// user id) so a ciphertext copied onto another user's row fails to decrypt.
    /// </param>
    string Protect(string plaintext, string context);

    /// <summary>
    /// Reverses <see cref="Protect"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/>
    /// if the payload was tampered with, was encrypted under an unknown key, or the context does not match.
    /// </summary>
    string Unprotect(string payload, string context);
}
