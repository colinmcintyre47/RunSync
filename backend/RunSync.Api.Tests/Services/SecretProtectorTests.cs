// Tests/Services/SecretProtectorTests.cs
// Unit tests for SecretProtector — the AES-256-GCM encryption that protects each user's
// Strava client secret at rest.
//
// These are the highest-stakes tests in the suite: a silent failure here means either
// unreadable credentials for every user, or plaintext secrets in the database.
//
// Covered:
//   - round-trip correctness (including unicode and empty input)
//   - ciphertext is non-deterministic (fresh nonce per encryption)
//   - the plaintext never appears in the stored payload
//   - tampering is detected (ciphertext, tag, and nonce)
//   - the AAD context binds ciphertext to one user — cross-user reuse fails
//   - key rotation: values written under a retired key still decrypt
//   - a fully unknown key fails closed
//   - misconfigured keys fail fast at construction

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using RunSync.Api.Models.Config;
using RunSync.Api.Services;
using Xunit;

namespace RunSync.Api.Tests.Services;

public class SecretProtectorTests
{
    // Deterministic test keys — these are throwaway values, never used outside tests.
    private static readonly string KeyA = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    private static readonly string KeyB = Convert.ToBase64String(Enumerable.Range(100, 32).Select(i => (byte)i).ToArray());

    private const string Context = "runsync:strava-client-secret:v1:1";

    // A realistic Strava client secret: 40 hex characters.
    private const string Secret = "0123456789abcdef0123456789abcdef01234567";

    private static SecretProtector Build(string masterKey, params string[] previousKeys)
        => new(Options.Create(new EncryptionConfig
        {
            MasterKey = masterKey,
            PreviousKeys = previousKeys
        }));

    [Fact]
    public void Protect_ThenUnprotect_ReturnsOriginalSecret()
    {
        SecretProtector sut = Build(KeyA);

        string payload = sut.Protect(Secret, Context);

        sut.Unprotect(payload, Context).Should().Be(Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("passphrase with spaces and symbols !@#$%^&*()")]
    [InlineData("unicode ✓ ünïcödé 走る")]
    public void Protect_ThenUnprotect_RoundTripsArbitraryInput(string plaintext)
    {
        SecretProtector sut = Build(KeyA);

        string payload = sut.Protect(plaintext, Context);

        sut.Unprotect(payload, Context).Should().Be(plaintext);
    }

    [Fact]
    public void Protect_SameInputTwice_ProducesDifferentCiphertext()
    {
        SecretProtector sut = Build(KeyA);

        string first = sut.Protect(Secret, Context);
        string second = sut.Protect(Secret, Context);

        // A fresh random nonce per call means identical plaintext must not yield identical
        // ciphertext — otherwise an observer could tell which users share a secret.
        first.Should().NotBe(second);

        sut.Unprotect(first, Context).Should().Be(Secret);
        sut.Unprotect(second, Context).Should().Be(Secret);
    }

    [Fact]
    public void Protect_PayloadDoesNotContainPlaintext()
    {
        SecretProtector sut = Build(KeyA);

        string payload = sut.Protect(Secret, Context);

        payload.Should().NotContain(Secret);
        payload.Should().StartWith("v1.");

        // Also assert on the decoded bytes — base64 could in principle hide a substring match.
        byte[] raw = Convert.FromBase64String(payload["v1.".Length..]);
        Encoding.UTF8.GetString(raw).Should().NotContain(Secret);
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        SecretProtector sut = Build(KeyA);
        string payload = sut.Protect(Secret, Context);

        byte[] raw = Convert.FromBase64String(payload["v1.".Length..]);
        // Flip a bit in the ciphertext region (after the 12-byte nonce and 16-byte tag)
        raw[^1] ^= 0xFF;
        string tampered = "v1." + Convert.ToBase64String(raw);

        Action act = () => sut.Unprotect(tampered, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_TamperedAuthenticationTag_Throws()
    {
        SecretProtector sut = Build(KeyA);
        string payload = sut.Protect(Secret, Context);

        byte[] raw = Convert.FromBase64String(payload["v1.".Length..]);
        raw[12] ^= 0xFF; // first byte of the 16-byte tag
        string tampered = "v1." + Convert.ToBase64String(raw);

        Action act = () => sut.Unprotect(tampered, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_TamperedNonce_Throws()
    {
        SecretProtector sut = Build(KeyA);
        string payload = sut.Protect(Secret, Context);

        byte[] raw = Convert.FromBase64String(payload["v1.".Length..]);
        raw[0] ^= 0xFF; // first byte of the 12-byte nonce
        string tampered = "v1." + Convert.ToBase64String(raw);

        Action act = () => sut.Unprotect(tampered, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_DifferentUserContext_Throws()
    {
        SecretProtector sut = Build(KeyA);

        // Encrypted for user 1...
        string payload = sut.Protect(Secret, "runsync:strava-client-secret:v1:1");

        // ...must not decrypt as user 2, even though the key is identical. This is what stops a
        // stolen ciphertext from being replayed onto another user's row.
        Action act = () => sut.Unprotect(payload, "runsync:strava-client-secret:v1:2");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_ValueWrittenUnderRetiredKey_StillDecrypts()
    {
        // Written when KeyB was the master key...
        SecretProtector before = Build(KeyB);
        string payload = before.Protect(Secret, Context);

        // ...and read after rotating to KeyA, with KeyB retained as a previous key.
        SecretProtector after = Build(KeyA, KeyB);

        after.Unprotect(payload, Context).Should().Be(Secret);
    }

    [Fact]
    public void Protect_AfterRotation_UsesNewMasterKeyOnly()
    {
        SecretProtector rotated = Build(KeyA, KeyB);
        string payload = rotated.Protect(Secret, Context);

        // A protector that only knows the retired key must not be able to read the new value —
        // proving new writes use the master key, not a previous one.
        SecretProtector retiredOnly = Build(KeyB);

        Action act = () => retiredOnly.Unprotect(payload, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Unprotect_UnknownKey_Throws()
    {
        string payload = Build(KeyB).Protect(Secret, Context);

        // KeyB is not configured at all here — no master, no previous.
        Action act = () => Build(KeyA).Unprotect(payload, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("not-a-recognised-payload")]
    [InlineData("v1.@@@not-base64@@@")]
    [InlineData("v1.")]                      // valid base64, but empty → truncated
    [InlineData("v2.AAAAAAAAAAAAAAAA")]      // unknown format version
    public void Unprotect_MalformedPayload_Throws(string payload)
    {
        SecretProtector sut = Build(KeyA);

        Action act = () => sut.Unprotect(payload, Context);

        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    public void Constructor_InvalidMasterKey_ThrowsWithActionableMessage(string masterKey)
    {
        Action act = () => Build(masterKey);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Encryption:MasterKey*");
    }

    [Fact]
    public void Constructor_MasterKeyOfWrongLength_Throws()
    {
        // 16 bytes — valid base64, but AES-128 rather than the required AES-256.
        string shortKey = Convert.ToBase64String(new byte[16]);

        Action act = () => Build(shortKey);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void Constructor_IgnoresEmptyPreviousKeyEntries()
    {
        // Environment-variable style config often leaves blank array slots behind;
        // they must not be treated as invalid keys.
        SecretProtector sut = Build(KeyA, "", "   ");

        string payload = sut.Protect(Secret, Context);

        sut.Unprotect(payload, Context).Should().Be(Secret);
    }
}
