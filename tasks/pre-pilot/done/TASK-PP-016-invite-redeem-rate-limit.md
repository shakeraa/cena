# PP-016: Wire Rate Limiting on Invite Code Redemption

- **Priority**: Medium — prevents brute-force code guessing
- **Complexity**: Senior engineer — integrate existing rate limit service
- **Source**: Expert panel review § Tenancy (Oren)

## Problem

`InviteLinkService` in `src/shared/Cena.Infrastructure/Auth/InviteLinkService.cs` declares a `Redeem(string code)` method, and the task spec says "rate-limited redeem." The `ShortCodeGenerator` produces 6-character codes from a 32-character alphabet — that is 32^6 = ~1 billion possible codes, which is resistant to random guessing but not to targeted brute-force if the attacker knows the alphabet and code length.

The rate limiter is not visible in the service implementation. If the endpoint that calls `Redeem` does not enforce rate limiting, an attacker could enumerate codes at high speed.

## Scope

### 1. Add rate limiting at the API endpoint level

The endpoint that accepts invite codes (likely in the student API or a dedicated join endpoint) should enforce:
- **Per-IP**: 10 redemption attempts per minute per IP address
- **Per-code**: 5 failed attempts per code before temporary lockout (15 minutes)
- **Global**: 100 redemption attempts per minute across all IPs

### 2. Add failed attempt tracking

```csharp
public sealed record InviteRedemptionResult
{
    // ... existing properties ...
    public int FailedAttempts { get; init; }  // NEW: for rate limit feedback
}
```

Track failed redemption attempts per code in a Redis counter with 15-minute TTL. After 5 failures, the code is temporarily locked even if the correct code is eventually submitted.

### 3. Response headers

Return appropriate headers on rate-limited responses:
- `429 Too Many Requests`
- `Retry-After: 900` (15 minutes for code lockout)
- `X-RateLimit-Remaining: N`

## Files to Modify

- The API endpoint that calls `IInviteLinkService.Redeem` (likely in student or admin API endpoints) — add rate limit checks before calling Redeem
- `src/shared/Cena.Infrastructure/Auth/InviteLinkService.cs` — add failed attempt counter (or handle in endpoint)

## Acceptance Criteria

- [ ] Per-IP rate limit of 10 attempts/minute enforced
- [ ] Per-code lockout after 5 failed attempts (15-minute cooldown)
- [ ] Correct 429 response with Retry-After header
- [ ] Test: 6 failed attempts on the same code → 6th attempt returns 429 even if code is valid
- [ ] Brute-force estimation: at 10 attempts/minute/IP, attacker needs ~1.8 million years to enumerate 32^6 codes
