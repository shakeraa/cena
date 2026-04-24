# TASK-TEN-P3f: Invite Link Machinery (JWT + Short Code + QR)

**Phase**: 3
**Priority**: normal
**Effort**: 2--3d
**Depends on**: TEN-P3a
**Blocks**: nothing
**Queue ID**: `t_43a5353a2a96`
**Assignee**: unassigned
**Status**: blocked

---

## Goal

Implement the invite link system for `InviteOnly` classrooms. Mentors generate signed invite links that contain a JWT with the classroom ID. Each link also has a 6-character short code for easy sharing and a QR code render endpoint. Redemption is rate-limited following the FIND-ux-006b pattern.

## Background

ADR-0001 defines `InviteOnly` as a join approval mode where no public code works -- only signed invite links grant access. This is the primary mechanism for `PersonalMentorship` classrooms (1-on-1 tutoring). The invite system must be secure (signed, expirable), user-friendly (short codes, QR), and abuse-resistant (rate-limited).

## Specification

### InstituteInviteDocument

Create `src/shared/Cena.Infrastructure/Documents/InstituteInviteDocument.cs`:

```csharp
namespace Cena.Infrastructure.Documents;

public enum InviteStatus { Active, Redeemed, Expired, Revoked }

public class InstituteInviteDocument
{
    public string Id { get; set; } = "";
    public string InviteId { get; set; } = "";
    public string ClassroomId { get; set; } = "";
    public string CreatedByMentorId { get; set; } = "";
    public string ShortCode { get; set; } = "";          // 6-char alphanumeric
    public string JwtToken { get; set; } = "";            // signed invite JWT
    public InviteStatus Status { get; set; } = InviteStatus.Active;
    public int MaxUses { get; set; } = 1;                 // 1 for personal, N for group
    public int CurrentUses { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? RedeemedByStudentId { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
}
```

### Invite JWT payload

```json
{
  "sub": "cena-invite",
  "classroom_id": "class-mentor-123",
  "invite_id": "inv-abc123",
  "mentor_id": "mentor-456",
  "max_uses": 1,
  "exp": 1714435200,
  "iat": 1714348800
}
```

- Signed with the server's HMAC key (from app configuration, NOT a hardcoded secret).
- Default expiry: 7 days. Configurable per invite.

### Short code table

6-character alphanumeric codes (A-Z, 0-9, excluding confusable characters: 0/O, 1/I/L). Alphabet: `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (30 chars, 30^6 = 729M combinations).

- Codes are unique per active invite (unique constraint on `ShortCode` where `Status == Active`).
- Collision on generation: retry with a new random code (max 3 retries, then fail).

### QR code render

`GET /api/invites/:shortCode/qr` returns a PNG QR code encoding the full invite URL: `https://app.cena.edu/join/{shortCode}`.

- QR generated server-side (use `QRCoder` NuGet package or equivalent).
- Cache the PNG for the invite's lifetime (ETag header).
- Image size: 300x300 pixels, PNG format.

### Events

```csharp
public record InviteCreated_V1(
    string InviteId,
    string ClassroomId,
    string CreatedByMentorId,
    string ShortCode,
    int MaxUses,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt
) : IDelegatedEvent;

public record InviteRedeemed_V1(
    string InviteId,
    string StudentId,
    string ClassroomId,
    DateTimeOffset RedeemedAt
) : IDelegatedEvent;

public record InviteRevoked_V1(
    string InviteId,
    string RevokedByMentorId,
    DateTimeOffset RevokedAt
) : IDelegatedEvent;
```

### REST endpoints

| Method | Path | Purpose | Auth | Rate limit |
|---|---|---|---|---|
| `POST` | `/api/mentor/invites` | Create invite | Mentor (classroom owner) | 10/hour |
| `GET` | `/api/mentor/invites?classroomId={id}` | List invites | Mentor | Standard |
| `DELETE` | `/api/mentor/invites/:id` | Revoke invite | Mentor | Standard |
| `GET` | `/api/invites/:shortCode` | Validate invite (public) | None | 20/min/IP |
| `POST` | `/api/invites/:shortCode/redeem` | Redeem invite | Student | 5/min/user |
| `GET` | `/api/invites/:shortCode/qr` | QR code PNG | None | 30/min/IP |

### Redemption flow

1. Student navigates to `/join/{shortCode}` (frontend route).
2. Frontend calls `GET /api/invites/{shortCode}` to validate -- returns classroom name, mentor name, program title (but NOT the JWT).
3. Student confirms and calls `POST /api/invites/{shortCode}/redeem`.
4. Backend validates JWT signature, checks expiry, checks `MaxUses > CurrentUses`.
5. If valid: emit `InviteRedeemed_V1` + `EnrollmentCreated_V1`, increment `CurrentUses`.
6. If `CurrentUses >= MaxUses`: set `Status = Redeemed`.

### Rate limiting

- Validate endpoint (`GET /api/invites/:shortCode`): 20 requests/min/IP (prevent brute-force code enumeration).
- Redeem endpoint (`POST /api/invites/:shortCode/redeem`): 5 requests/min/user (prevent abuse).
- Follow the FIND-ux-006b pattern: return 429 with `Retry-After` header.

## Implementation notes

- The HMAC signing key must come from `IConfiguration["Cena:InviteSigningKey"]`. Never hardcode.
- Follow FIND-sec-005: validate invite ownership before listing/revoking.
- Follow FIND-data-007 CQRS pattern: create/redeem/revoke emit events, list/validate are queries.
- The QR endpoint is public (no auth) -- it only embeds the URL, which itself requires authentication to redeem.
- Short codes are case-insensitive for user convenience (normalize to uppercase on lookup).

## Quality requirements

No stubs, no canned data, no placeholder implementations. Every code path must handle real data, real errors, real edge cases. The invite system is a security surface -- JWT signing, expiry enforcement, rate limiting, and short code entropy must all be production-grade. Follow FIND-ux-006b rate limiting pattern. Follow FIND-data-005 event naming convention.

## Tests required

**Test class**: `InviteLinkTests` in `src/api/Cena.Admin.Api.Tests/InviteLinkTests.cs`

| Test method | Assertion |
|---|---|
| `CreateInvite_ReturnsShortCodeAndJwt` | Create invite, assert 201 with `ShortCode` (6 chars) and `JwtToken` (valid JWT). |
| `CreateInvite_ShortCodeIsUnique` | Create 100 invites, assert all short codes unique. |
| `ValidateInvite_ReturnsClassroomInfo` | Valid short code, assert response includes classroom name + mentor name. |
| `ValidateInvite_ExpiredInvite_Returns410` | Expired invite, assert 410 Gone. |
| `RedeemInvite_ValidJwt_CreatesEnrollment` | Redeem with valid JWT, assert `EnrollmentDocument` created. |
| `RedeemInvite_MaxUsesReached_Returns409` | Invite with `MaxUses=1` already redeemed, assert 409 Conflict. |
| `RedeemInvite_InvalidSignature_Returns401` | Tampered JWT, assert 401. |
| `RevokeInvite_SetsRevokedStatus` | Revoke invite, assert `Status == Revoked`. |
| `RedeemInvite_RevokedInvite_Returns410` | Redeem revoked invite, assert 410. |
| `QrEndpoint_ReturnsPng` | Request QR, assert `Content-Type: image/png` and non-empty body. |
| `RateLimit_ValidateEndpoint_Returns429` | Exceed 20/min, assert 429 with `Retry-After`. |

## Definition of Done

- [ ] `InstituteInviteDocument` created with full lifecycle
- [ ] 3 new events registered in MartenConfiguration
- [ ] 6 REST endpoints implemented
- [ ] JWT signing with configurable HMAC key (not hardcoded)
- [ ] 6-char short code generation with collision retry
- [ ] QR code PNG render endpoint
- [ ] Rate limiting on validate (20/min/IP) and redeem (5/min/user)
- [ ] Expired/revoked/max-uses enforcement
- [ ] All 11 tests pass
- [ ] `dotnet build` succeeds with zero warnings
- [ ] No `// TODO` or `// Phase N` comments
- [ ] No hardcoded secrets (grep-verifiable)

## Files to read first

1. `docs/adr/0001-multi-institute-enrollment.md` -- invite system spec
2. `src/shared/Cena.Infrastructure/Documents/ClassroomDocument.cs` -- `JoinApprovalMode`
3. `src/shared/Cena.Infrastructure/Auth/` -- existing auth patterns
4. `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` -- existing join-code flow

## Files to create / modify

| File path | Action | What changes |
|---|---|---|
| `src/shared/Cena.Infrastructure/Documents/InstituteInviteDocument.cs` | create | Invite document + status enum |
| `src/shared/Cena.Infrastructure/Auth/InviteJwtService.cs` | create | JWT sign/verify for invites |
| `src/shared/Cena.Infrastructure/Invites/ShortCodeGenerator.cs` | create | 6-char code generation |
| `src/actors/Cena.Actors/Events/EnrollmentEvents.cs` | modify | Add 3 invite events |
| `src/actors/Cena.Actors/Configuration/MartenConfiguration.cs` | modify | Register document + events |
| `src/api/Cena.Admin.Api/InviteService.cs` | create | Mentor invite CRUD |
| `src/api/Cena.Student.Api.Host/Endpoints/InviteEndpoints.cs` | create | Validate/redeem/QR endpoints |
| `src/api/Cena.Admin.Api.Tests/InviteLinkTests.cs` | create | 11 tests |
