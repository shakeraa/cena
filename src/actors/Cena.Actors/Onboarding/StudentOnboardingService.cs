// =============================================================================
// Cena Platform — Student first-sign-in onboarding service
// TASK-E2E-A-01-BE-01 + BE-02: bridge between Firebase emulator/prod sign-up
// and Cena's tenant-aware AdminUser + StudentProfile state.
//
// Flow on first call:
//   1. Validate uid + tenant + role (E2E trusted-mode gate or invite-code path)
//   2. Set Firebase custom claims (role + tenant_id + school_id) so downstream
//      /api/me, /api/admin/* etc. see a fully-claimed JWT after the SPA's
//      forced idToken refresh
//   3. Create AdminUser Marten doc (id == Firebase uid, Role = STUDENT). This
//      also unblocks TASK-E2E-A-05 (role-claim invalidation), which needs an
//      AdminUser row to exist before AdminRoleService.AssignRoleToUserAsync
//      can flip the role.
//   4. Append StudentOnboardedV1 to the student's event stream (Marten),
//      bootstrapping StudentProfileSnapshot for downstream /api/me/profile
//      reads.
//   5. Publish StudentOnboardedV1 on NATS subject
//      cena.events.student.{uid}.onboarded with `tenant_id` header so the
//      e2e-flow bus probe (and any per-tenant fanout) can filter by tenant.
//
// Idempotency: keyed off the AdminUser document. A second POST returns the
// existing snapshot (no second Firebase claim push, no second event emit).
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Firebase;
using Marten;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Onboarding;

/// <summary>
/// Validated request shape — the endpoint validates input at the system
/// boundary, the service trusts what it receives. Production callers
/// (invite-code path) supply a verified <see cref="TenantId"/>; the E2E trusted
/// mode supplies it directly from the SPA fixture.
/// </summary>
public sealed record StudentOnboardingRequest(
    string Uid,
    string Email,
    string TenantId,
    string SchoolId,
    string? DisplayName,
    Guid CorrelationId);

/// <summary>
/// Outcome record returned to the endpoint. <see cref="WasNewlyOnboarded"/>
/// distinguishes a freshly-onboarded user (claims pushed, event emitted) from
/// an idempotent re-call (no side-effects beyond the lookup). The endpoint
/// returns 200 in both cases — the client can't tell which happened — but
/// the metric/log layer benefits from knowing.
/// </summary>
public sealed record StudentOnboardingResult(
    string Uid,
    string TenantId,
    string SchoolId,
    string Role,
    bool WasNewlyOnboarded);

public interface IStudentOnboardingService
{
    /// <summary>
    /// Idempotent. First call provisions claims + AdminUser + StudentProfile
    /// + emits StudentOnboardedV1. Subsequent calls return the existing state.
    /// </summary>
    Task<StudentOnboardingResult> OnboardAsync(StudentOnboardingRequest request, CancellationToken ct);
}

public sealed class StudentOnboardingService : IStudentOnboardingService
{
    private readonly IDocumentStore _store;
    private readonly IFirebaseAdminService _firebase;
    private readonly INatsConnection _nats;
    private readonly ILogger<StudentOnboardingService> _logger;
    private readonly TimeProvider _clock;

    private static readonly Meter Meter = new("Cena.Onboarding", "1.0.0");
    private static readonly Counter<long> NewlyOnboarded =
        Meter.CreateCounter<long>("cena_student_newly_onboarded_total");
    private static readonly Counter<long> IdempotentReplays =
        Meter.CreateCounter<long>("cena_student_onboard_idempotent_total");
    private static readonly Counter<long> NatsPublishFailures =
        Meter.CreateCounter<long>("cena_student_onboarded_nats_failures_total");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public StudentOnboardingService(
        IDocumentStore store,
        IFirebaseAdminService firebase,
        INatsConnection nats,
        ILogger<StudentOnboardingService> logger,
        TimeProvider? clock = null)
    {
        _store = store;
        _firebase = firebase;
        _nats = nats;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<StudentOnboardingResult> OnboardAsync(
        StudentOnboardingRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Service-level invariant checks. The endpoint already validated input
        // shape; these guard against test-double misuse.
        if (string.IsNullOrWhiteSpace(request.Uid))
            throw new ArgumentException("Uid is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("Email is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SchoolId))
            throw new ArgumentException("SchoolId is required.", nameof(request));

        // ── Idempotency gate ──
        // AdminUser doc keyed by Firebase uid. Existence implies a previous
        // successful onboarding; re-pushing claims and re-emitting the event
        // would corrupt downstream consumers that dedup by event_id.
        await using var query = _store.QuerySession();
        var existing = await query.LoadAsync<AdminUser>(request.Uid, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            // Collision check: same uid but a non-student role means someone
            // (admin tool, prior bootstrap) already claimed this identity for
            // a different purpose. Refusing here keeps the contract honest —
            // the endpoint surfaces a 409 rather than silently downgrading.
            if (existing.Role != CenaRole.STUDENT)
            {
                throw new InvalidOperationException(
                    $"Uid {request.Uid} already exists as {existing.Role}, refusing to re-bind as STUDENT.");
            }

            // Idempotent re-call: still re-push Firebase claims. The
            // claims-transformer + RoleAuthorizationGuard treat the Firebase
            // claim as authoritative; if a previous run wrote a stale value
            // (bad casing, wrong tenant) Marten's AdminUser is still the
            // canonical record but the JWT carries the wrong role. Re-pushing
            // on every idempotent call keeps the two in sync.
            await _firebase.SetCustomClaimsAsync(request.Uid, new Dictionary<string, object>
            {
                ["role"] = "STUDENT",
                ["tenant_id"] = request.TenantId,
                ["school_id"] = existing.School ?? request.SchoolId,
            }).ConfigureAwait(false);

            IdempotentReplays.Add(1);
            _logger.LogInformation(
                "[ONBOARDING] uid={Uid} tenant={TenantId} school={SchoolId} idempotent re-call (already onboarded; claims refreshed)",
                request.Uid, request.TenantId, existing.School ?? "(unset)");

            return new StudentOnboardingResult(
                Uid: existing.Id,
                TenantId: request.TenantId,
                SchoolId: existing.School ?? request.SchoolId,
                Role: existing.Role.ToString().ToLowerInvariant(),
                WasNewlyOnboarded: false);
        }

        // ── Firebase custom claims ──
        // Push claims FIRST. If Marten persistence fails after this, the uid
        // has stale claims for a tenant that has no AdminUser — recoverable
        // (next call sees no AdminUser, retries cleanly). If Marten persistence
        // succeeded but Firebase failed, the user gets a 401 loop — much worse
        // recovery story.
        //
        // Role is the uppercase CenaRole enum string ("STUDENT") to match the
        // claims-transformer + RoleAuthorizationGuard. Lowercase fails with
        // CENA_AUTH_IDOR_VIOLATION on every /api/me/* call.
        await _firebase.SetCustomClaimsAsync(request.Uid, new Dictionary<string, object>
        {
            ["role"] = "STUDENT",
            ["tenant_id"] = request.TenantId,
            ["school_id"] = request.SchoolId,
        }).ConfigureAwait(false);

        var onboardedAt = _clock.GetUtcNow();

        // ── Marten: AdminUser doc + StudentOnboardedV1 event in one tx ──
        await using var session = _store.LightweightSession();

        var adminUser = new AdminUser
        {
            Id = request.Uid,
            Email = request.Email,
            FullName = request.DisplayName ?? request.Email,
            Role = CenaRole.STUDENT,
            Status = UserStatus.Active,
            School = request.SchoolId,
            CreatedAt = onboardedAt,
            LastLoginAt = onboardedAt,
        };
        session.Store(adminUser);

        var onboardedEvent = new StudentOnboardedV1(
            Uid: request.Uid,
            Email: request.Email,
            TenantId: request.TenantId,
            SchoolId: request.SchoolId,
            OnboardedAt: onboardedAt,
            CorrelationId: request.CorrelationId);

        // Stream key = Firebase uid. Lines up with StudentProfileSnapshot's
        // existing convention (Id == StudentId == uid) and with every
        // downstream "for this student" event sourced under the same key.
        session.Events.Append(request.Uid, onboardedEvent);

        await session.SaveChangesAsync(ct).ConfigureAwait(false);

        // ── NATS publish (best-effort, after persistence) ──
        // Pattern matches SessionNatsPublisher: Marten is the source of truth,
        // NATS is a fanout signal. Failures here log + metric but do not roll
        // back the onboarding.
        await PublishOnboardedEventAsync(onboardedEvent, ct).ConfigureAwait(false);

        NewlyOnboarded.Add(1);
        _logger.LogInformation(
            "[ONBOARDING] uid={Uid} tenant={TenantId} school={SchoolId} new student onboarded (correlationId={CorrelationId})",
            request.Uid, request.TenantId, request.SchoolId, request.CorrelationId);

        return new StudentOnboardingResult(
            Uid: request.Uid,
            TenantId: request.TenantId,
            SchoolId: request.SchoolId,
            Role: "student",
            WasNewlyOnboarded: true);
    }

    private async Task PublishOnboardedEventAsync(StudentOnboardedV1 evt, CancellationToken ct)
    {
        var subject = NatsSubjects.StudentEvent(evt.Uid, NatsSubjects.StudentOnboarded);
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(evt, JsonOpts);
            var headers = new NatsHeaders
            {
                ["Nats-Msg-Id"] = $"onboarded-{evt.Uid}",
                ["Cena-Schema-Version"] = "1",
                ["tenant_id"] = evt.TenantId,
                ["school_id"] = evt.SchoolId,
                ["correlation_id"] = evt.CorrelationId.ToString("N"),
            };

            // Deadline-bounded: NATS.NET v2 blocks on a disconnected connection
            // waiting for reconnect. Without the deadline the on-first-sign-in
            // request hangs indefinitely on a NATS outage. Marten is canonical;
            // a missed publish is logged + metric'd and recoverable via outbox
            // backfill (E2E-J-03 invariant).
            await _nats.PublishWithDeadlineAsync(subject, data, headers: headers, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NatsPublishFailures.Add(1);
            _logger.LogError(ex,
                "[ONBOARDING] NATS publish failed for {Subject}; Marten remains source of truth",
                subject);
            // Do NOT rethrow — Marten is canonical. A backfill job (out of
            // scope for this task) can re-publish the event from the stream.
        }
    }
}
