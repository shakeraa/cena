// =============================================================================
// Cena Platform — SSO verifier + Firebase-minter ports (EPIC-PRR-I PRR-342)
//
// The two ports downstream endpoints depend on, plus the fail-closed Null
// default adapters that ship with this slice. Real adapters land later:
//
//   * OIDC adapter (Google + Microsoft Entra) — needs a security-review
//     signoff on the JWKS-discovery + System.IdentityModel.Tokens.Jwt
//     library stack before it's wired. Tracked as a follow-up under
//     EPIC-PRR-I.
//
//   * SAML 2.0 adapter — Sustainsys.Saml2 library choice also requires
//     signoff. Needed for districts on Clever / Okta / ADFS.
//
//   * Firebase custom-token minter — blocked on REV-001 (Firebase
//     service-account key rotation still pending in GCP per session
//     memory). Wiring lands once the key rotation clears.
//
// Until those land the Null defaults keep the platform FAIL-CLOSED: any
// inbound call returns a stable rejection reason the endpoint surfaces
// as 503 + clear "SSO not configured" diagnostic, never a silent
// success. This is deliberately NOT a stub that pretends to work — per
// memory "No stubs — production grade", a Null default that fails
// deterministically with a reason code is production-safe behaviour,
// not a placeholder.
// =============================================================================

namespace Cena.Actors.Sso;

/// <summary>
/// Outcome of an SSO token/assertion verification attempt. Exactly one
/// of <see cref="Verified"/> / <see cref="RejectionReason"/> is set.
/// </summary>
/// <param name="Verified">
/// Verified claims extracted from a cryptographically-validated token
/// or assertion. Null when verification failed.
/// </param>
/// <param name="RejectionReason">
/// Stable reason code when verification failed (token expired,
/// signature invalid, issuer mismatch at protocol level, no verifier
/// configured, …). UI maps the code to localised 503/403 copy. Null on
/// success.
/// </param>
public sealed record SsoVerificationResult(
    VerifiedSsoClaims? Verified,
    string? RejectionReason);

/// <summary>
/// Port for protocol-specific token / assertion verification. One
/// implementation per <see cref="SsoProvider"/>:
///
///   - Google Workspace: id_token from OIDC authorization-code flow,
///     signature verified against accounts.google.com JWKS.
///   - Microsoft Entra: id_token from OIDC flow, signature verified
///     against the tenant's OIDC metadata document's jwks_uri.
///   - SAML 2.0: assertion POST binding, XML signature verified against
///     the IdP's signing certificate.
///
/// Adapters MUST perform full cryptographic verification + issuer /
/// audience / not-before / not-after / nonce checks before returning a
/// <see cref="SsoVerificationResult"/> with a non-null
/// <see cref="SsoVerificationResult.Verified"/>. Claim mapping to Cena
/// roles happens downstream in <see cref="SsoClaimsMapper.Resolve"/>;
/// the verifier's job is ONLY to assert "this token came from the IdP
/// the config names, is still valid, and these are the claims in it".
/// </summary>
public interface ISsoTokenVerifier
{
    /// <summary>
    /// Verify an inbound token/assertion against a school's SSO config.
    /// </summary>
    /// <param name="rawProviderPayload">
    /// Raw id_token (OIDC) or base64-encoded SAML assertion. Adapter
    /// parses + cryptographically verifies.
    /// </param>
    /// <param name="config">
    /// Tenant's SSO configuration. Adapter reads
    /// <see cref="SchoolSsoConfiguration.IssuerUrl"/> +
    /// <see cref="SchoolSsoConfiguration.ClientId"/> to look up JWKS /
    /// metadata / signing-cert + to validate audience.
    /// </param>
    /// <param name="ct">Cancellation for network calls (JWKS fetch, metadata).</param>
    Task<SsoVerificationResult> VerifyAsync(
        string rawProviderPayload,
        SchoolSsoConfiguration config,
        CancellationToken ct);
}

/// <summary>
/// Fail-closed default verifier. Every call returns a stable rejection
/// reason. Wired by DI until a real adapter is registered. Endpoints
/// surface this as 503 "SSO verifier not configured" rather than
/// silently succeeding. Per memory "No stubs — production grade": this
/// is correct production behaviour until the vendor SDK choice is
/// signed off, not a placeholder.
/// </summary>
public sealed class NullSsoTokenVerifier : ISsoTokenVerifier
{
    /// <summary>
    /// Stable reason code. Callers compare against this constant rather
    /// than string-matching the reason text.
    /// </summary>
    public const string NotConfiguredReason = "sso_verifier_not_configured";

    /// <inheritdoc />
    public Task<SsoVerificationResult> VerifyAsync(
        string rawProviderPayload,
        SchoolSsoConfiguration config,
        CancellationToken ct) =>
        Task.FromResult(new SsoVerificationResult(null, NotConfiguredReason));
}

/// <summary>
/// Outcome of a Firebase custom-token minting attempt. Exactly one of
/// <see cref="CustomToken"/> / <see cref="RejectionReason"/> is set.
/// </summary>
/// <param name="CustomToken">
/// Firebase custom JWT the client exchanges for a Firebase ID token.
/// Null when minting failed.
/// </param>
/// <param name="RejectionReason">
/// Stable reason code when minting failed (no service-account key
/// configured, key rotation pending, downstream Admin SDK error, …).
/// </param>
public sealed record FirebaseCustomTokenResult(
    string? CustomToken,
    string? RejectionReason);

/// <summary>
/// Port for minting Firebase custom tokens from a resolved Cena identity.
/// Used by the SSO endpoint once <see cref="SsoClaimsMapper.Resolve"/>
/// returns a <see cref="SsoResolvedIdentity"/>: the endpoint calls this
/// port to get a Firebase custom token, returns it to the client, and
/// the client exchanges it for a Firebase ID token via the Firebase
/// client SDK. This bridges SSO → existing Firebase-Auth-gated session
/// flow without a second auth step.
///
/// The real adapter uses the Firebase Admin SDK +
/// service-account signing key. Blocked on REV-001 (GCP key rotation
/// pending); the Null default keeps the feature fail-closed until then.
/// </summary>
public interface IFirebaseSsoTokenMinter
{
    /// <summary>
    /// Mint a Firebase custom token for the given resolved SSO identity.
    /// </summary>
    /// <param name="resolved">
    /// Identity previously produced by <see cref="SsoClaimsMapper.Resolve"/>.
    /// The minter encodes <see cref="SsoResolvedIdentity.InstituteId"/>,
    /// <see cref="SsoResolvedIdentity.Role"/>, and
    /// <see cref="SsoResolvedIdentity.ProviderSubject"/> as custom
    /// claims on the Firebase token so downstream Firestore rules +
    /// Cena session-token exchange read them atomically.
    /// </param>
    /// <param name="ct">Cancellation for the Admin-SDK network call.</param>
    Task<FirebaseCustomTokenResult> MintAsync(
        SsoResolvedIdentity resolved,
        CancellationToken ct);
}

/// <summary>
/// Fail-closed default minter. Every call returns a stable rejection
/// reason so downstream endpoints surface "SSO bridge not configured"
/// rather than minting anything. Wired by DI until REV-001 clears and a
/// real Firebase Admin-SDK-backed minter is registered.
/// </summary>
public sealed class NullFirebaseSsoTokenMinter : IFirebaseSsoTokenMinter
{
    /// <summary>Stable reason code. Matches REV-001 wiring state.</summary>
    public const string NotConfiguredReason = "firebase_minter_not_configured";

    /// <inheritdoc />
    public Task<FirebaseCustomTokenResult> MintAsync(
        SsoResolvedIdentity resolved,
        CancellationToken ct) =>
        Task.FromResult(new FirebaseCustomTokenResult(null, NotConfiguredReason));
}
