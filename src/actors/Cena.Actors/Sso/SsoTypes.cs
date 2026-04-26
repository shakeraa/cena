// =============================================================================
// Cena Platform — SSO types + per-school configuration (EPIC-PRR-I PRR-342)
//
// Pure data types + the tenant-level configuration model for the three
// SSO protocols the task names:
//
//   - Google Workspace for Education (OIDC)
//   - Microsoft Entra                 (OIDC)
//   - SAML 2.0                        (for districts on Clever/Okta/ADFS)
//
// This file carries ONLY the non-dangerous parts:
//   - enums + records (no I/O, no secrets, no network calls)
//   - pure claims-to-Cena-role mapping policy
//   - in-memory tenant-config store
//
// The vendor-SDK wiring (System.IdentityModel.Tokens.Jwt for Google/
// Microsoft OIDC signature verification, Sustainsys.Saml2 for SAML XML
// signature verification) lives in adapter files that get added AFTER
// the library choice is signed off by security review. The verifier +
// minter PORTS are declared in the sibling SsoPorts.cs file so adapter
// files slot in without endpoint churn; the default adapters are
// NullSsoTokenVerifier + NullFirebaseSsoTokenMinter which reject every
// inbound call with a clear reason code. This keeps the feature
// FAIL-CLOSED until a real adapter is wired — the opposite of the
// dangerous "stub that pretends to work" pattern.
//
// Firebase-custom-token bridge: declared as a port
// (IFirebaseSsoTokenMinter) with a NullFirebaseSsoTokenMinter default
// because REV-001 (Firebase service-account key rotation) is still
// pending per session memory. Production wiring lands after the key
// rotation clears.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Sso;

/// <summary>Supported SSO provider protocols. Persisted on tenant config docs.</summary>
public enum SsoProvider
{
    /// <summary>
    /// Google Workspace for Education (OpenID Connect over OAuth 2.0).
    /// Majority of K-12 schools in Cena's launch market.
    /// </summary>
    GoogleWorkspace = 1,

    /// <summary>
    /// Microsoft Entra (formerly Azure AD), OpenID Connect.
    /// Common for Office 365 / Teams for Education schools.
    /// </summary>
    MicrosoftEntra = 2,

    /// <summary>
    /// SAML 2.0. Large US districts on Clever / Okta / ADFS; some
    /// Israeli universities. Assertion-post binding with XML signature.
    /// </summary>
    Saml2 = 3,
}

/// <summary>
/// Cena role mapped from the IdP's group / affiliation claim. Matches
/// ADR-0001 multi-institute role convention. Students get the student
/// app; Teachers get the teacher console; SchoolAdmins get the
/// classroom + roster admin.
/// </summary>
public enum SsoMappedRole
{
    /// <summary>Default when no group matches. Endpoint rejects login.</summary>
    Unknown = 0,
    Student = 1,
    Teacher = 2,
    SchoolAdmin = 3,
}

/// <summary>
/// One group-name → Cena-role row on a school's SSO configuration. The
/// IdP sends group memberships as a claim (OIDC <c>"groups"</c>, SAML
/// <c>"urn:oasis:names:tc:SAML:attribute:Role"</c>). Each row says:
/// "if the IdP says this person belongs to group X, treat them as Cena
/// role Y."
/// </summary>
public sealed record SsoRoleGroupMapping(
    string IdpGroupName,
    SsoMappedRole CenaRole);

/// <summary>
/// Per-school tenant SSO configuration. One document per
/// institute/school, keyed by Cena <c>InstituteId</c>.
/// </summary>
/// <param name="InstituteId">Cena-internal tenant id (ADR-0001).</param>
/// <param name="Provider">Which protocol the school authenticates against.</param>
/// <param name="IssuerUrl">
/// OIDC issuer or SAML IdP entity id. For Google:
/// <c>https://accounts.google.com</c>; for Microsoft tenant:
/// <c>https://login.microsoftonline.com/{tenantId}/v2.0</c>; for SAML:
/// the IdP-issued entity id.
/// </param>
/// <param name="ClientId">
/// OIDC client id (Google OAuth client id / Microsoft Entra app id) or
/// SAML service-provider entity id. NEVER a secret — the client-secret
/// for OIDC lives in a separate secret-store record keyed by
/// <see cref="ClientId"/>; this config doc must be safe to serialize
/// to Marten without PII/credential scrubbing.
/// </param>
/// <param name="AllowedHostedDomain">
/// Optional email-domain allowlist. For Google: the <c>hd</c> claim
/// must match (e.g. <c>"school.edu.il"</c>) — rejects personal gmail
/// accounts signing into a school SSO flow. Null = no domain check.
/// </param>
/// <param name="RoleMappings">
/// Group-name → Cena-role mappings. First match wins. An IdP group not
/// in this list degrades the user to <see cref="SsoMappedRole.Unknown"/>
/// and login is rejected.
/// </param>
/// <param name="DefaultClassroomId">
/// Optional — classroom every provisioned student joins by default.
/// Used by schools that don't send roster claims. Null = student is
/// provisioned but not auto-enrolled anywhere (school admin completes
/// roster assignment later).
/// </param>
public sealed record SchoolSsoConfiguration(
    string InstituteId,
    SsoProvider Provider,
    string IssuerUrl,
    string ClientId,
    string? AllowedHostedDomain,
    IReadOnlyList<SsoRoleGroupMapping> RoleMappings,
    string? DefaultClassroomId);

/// <summary>
/// Claims extracted from a verified IdP token / assertion. Feeds the
/// <see cref="SsoClaimsMapper"/>. Populated by the protocol-specific
/// verifier: OIDC id_token → claims dictionary; SAML assertion → same
/// shape. Downstream JIT provisioning never sees the raw token.
/// </summary>
/// <param name="IssuerUrl">Verified issuer (must match config).</param>
/// <param name="Subject">Provider-stable subject identifier (OIDC <c>sub</c>, SAML <c>NameID</c>).</param>
/// <param name="Email">User's email (lowercased, trimmed).</param>
/// <param name="HostedDomain">Google <c>hd</c> claim; null for non-Google.</param>
/// <param name="DisplayName">Optional full name from the IdP.</param>
/// <param name="Groups">IdP group-memberships to map.</param>
public sealed record VerifiedSsoClaims(
    string IssuerUrl,
    string Subject,
    string Email,
    string? HostedDomain,
    string? DisplayName,
    IReadOnlyList<string> Groups);

/// <summary>
/// Outcome of mapping verified claims against a school's SSO config.
/// Exactly one of <see cref="Resolved"/> (Cena role + institute id) or
/// <see cref="RejectionReason"/> is set.
/// </summary>
public sealed record SsoMappingResult(
    SsoResolvedIdentity? Resolved,
    string? RejectionReason);

/// <summary>The per-student resolved identity after a successful mapping.</summary>
public sealed record SsoResolvedIdentity(
    string InstituteId,
    SsoMappedRole Role,
    string ProviderSubject,
    string Email,
    string? DisplayName,
    string? DefaultClassroomId);

/// <summary>
/// Pure claims-mapping policy. Takes verified claims + a school config,
/// returns either the resolved identity or a stable rejection reason
/// the endpoint surfaces as 403 with no further info.
/// </summary>
public static class SsoClaimsMapper
{
    /// <summary>Issuer verified-against-config mismatch.</summary>
    public const string IssuerMismatchReason = "sso_issuer_mismatch";

    /// <summary>Hosted-domain on Google claim does not match school allowlist.</summary>
    public const string HostedDomainRejectedReason = "sso_hosted_domain_rejected";

    /// <summary>No IdP group maps to a Cena role (user not provisioned at this school).</summary>
    public const string NoRoleMatchReason = "sso_no_role_match";

    /// <summary>Email claim missing or malformed.</summary>
    public const string InvalidEmailReason = "sso_invalid_email";

    /// <summary>Subject claim missing.</summary>
    public const string InvalidSubjectReason = "sso_invalid_subject";

    /// <summary>
    /// Resolve claims against config. All checks are explicit so a future
    /// reviewer can read the policy top-to-bottom. Rejection is always
    /// fail-closed — ambiguous cases reject rather than defaulting to
    /// <see cref="SsoMappedRole.Student"/>.
    /// </summary>
    public static SsoMappingResult Resolve(
        VerifiedSsoClaims claims,
        SchoolSsoConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(config);

        // Subject + email are the two "this person exists" signals.
        if (string.IsNullOrWhiteSpace(claims.Subject))
        {
            return new SsoMappingResult(null, InvalidSubjectReason);
        }
        if (string.IsNullOrWhiteSpace(claims.Email) || !claims.Email.Contains('@'))
        {
            return new SsoMappingResult(null, InvalidEmailReason);
        }

        // Issuer cross-check — defense-in-depth even though the
        // verifier should have validated against JWKS/metadata already.
        if (!string.Equals(
                claims.IssuerUrl?.TrimEnd('/'),
                config.IssuerUrl?.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
        {
            return new SsoMappingResult(null, IssuerMismatchReason);
        }

        // Google-specific: hosted-domain allowlist. Reject personal gmail
        // accounts if the school declared a domain.
        if (config.Provider == SsoProvider.GoogleWorkspace &&
            !string.IsNullOrWhiteSpace(config.AllowedHostedDomain))
        {
            if (!string.Equals(
                    claims.HostedDomain,
                    config.AllowedHostedDomain,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new SsoMappingResult(null, HostedDomainRejectedReason);
            }
        }

        // Role mapping — first matching group wins. Fail-closed default:
        // unmapped → reject with stable reason, never silently become
        // Student.
        SsoMappedRole role = SsoMappedRole.Unknown;
        if (claims.Groups is not null)
        {
            foreach (var g in claims.Groups)
            {
                foreach (var m in config.RoleMappings)
                {
                    if (string.Equals(g, m.IdpGroupName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (m.CenaRole == SsoMappedRole.Unknown) continue;
                        // Prefer higher-privilege role when multiple groups match.
                        if ((int)m.CenaRole > (int)role)
                        {
                            role = m.CenaRole;
                        }
                    }
                }
            }
        }

        if (role == SsoMappedRole.Unknown)
        {
            return new SsoMappingResult(null, NoRoleMatchReason);
        }

        return new SsoMappingResult(
            new SsoResolvedIdentity(
                InstituteId: config.InstituteId,
                Role: role,
                ProviderSubject: claims.Subject,
                Email: claims.Email.Trim().ToLowerInvariant(),
                DisplayName: claims.DisplayName,
                DefaultClassroomId: config.DefaultClassroomId),
            null);
    }
}

/// <summary>
/// Per-school SSO configuration store. Admin endpoint writes the config
/// on school onboarding; the verifier + mapper read it on every login.
/// InMemory impl is production-grade for single-host; Marten impl is a
/// follow-up (matches the ADR-0042 store-pattern used elsewhere).
/// </summary>
public interface ISchoolSsoConfigurationStore
{
    Task<SchoolSsoConfiguration?> GetByInstituteIdAsync(string instituteId, CancellationToken ct);
    Task SaveAsync(SchoolSsoConfiguration config, CancellationToken ct);
    Task<IReadOnlyList<SchoolSsoConfiguration>> ListAllAsync(CancellationToken ct);
}

/// <inheritdoc />
public sealed class InMemorySchoolSsoConfigurationStore : ISchoolSsoConfigurationStore
{
    private readonly ConcurrentDictionary<string, SchoolSsoConfiguration> _byInstitute = new();

    public Task<SchoolSsoConfiguration?> GetByInstituteIdAsync(
        string instituteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return Task.FromResult<SchoolSsoConfiguration?>(null);
        _byInstitute.TryGetValue(instituteId, out var cfg);
        return Task.FromResult(cfg);
    }

    public Task SaveAsync(SchoolSsoConfiguration config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.InstituteId))
            throw new ArgumentException("InstituteId is required.", nameof(config));
        _byInstitute[config.InstituteId] = config;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SchoolSsoConfiguration>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SchoolSsoConfiguration>>(
            _byInstitute.Values.ToList());
}
