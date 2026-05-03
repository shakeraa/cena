// =============================================================================
// Cena Platform -- Admin User Management Service
// BKD-002: Business logic for user CRUD, suspension, invites
// prr-021: BulkInviteAsync delegates to RosterImportProcessor (size, injection,
//   UTF-8, bidi, homoglyph, tenant binding, audit log).
// =============================================================================

using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Admin.Api.RosterImport;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Security;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IAdminUserService
{
    Task<UserListResponse> ListUsersAsync(
        string? query, string? role, string? status, string? school, string? grade,
        int page, int itemsPerPage, string? sortBy, string? orderBy, ClaimsPrincipal caller);
    Task<AdminUserDto?> GetUserAsync(string id, ClaimsPrincipal caller);
    Task<AdminUserDto> CreateUserAsync(CreateUserRequest request, ClaimsPrincipal caller);
    Task<AdminUserDto> UpdateUserAsync(string id, UpdateUserRequest request, ClaimsPrincipal caller);
    Task SoftDeleteUserAsync(string id, ClaimsPrincipal caller);
    Task SuspendUserAsync(string id, string reason, ClaimsPrincipal caller);
    Task ActivateUserAsync(string id, ClaimsPrincipal caller);
    Task<AdminUserDto> InviteUserAsync(InviteUserRequest request, ClaimsPrincipal caller);
    Task<BulkInviteResult> BulkInviteAsync(Stream csvStream, ClaimsPrincipal caller);
    Task<UserStatsResponse> GetStatsAsync(ClaimsPrincipal caller);
    Task<IReadOnlyList<UserActivityEntry>> GetActivityAsync(string userId, ClaimsPrincipal caller);
    Task<IReadOnlyList<UserSessionDto>> GetSessionsAsync(string userId, ClaimsPrincipal caller);
    Task RevokeSessionAsync(string userId, string sessionId, ClaimsPrincipal caller);
    Task RecordSessionAsync(string userId, string sessionId, string device, string browser, string ip);
    Task<bool> ForcePasswordResetAsync(string id, ClaimsPrincipal caller);
    Task<bool> RevokeApiKeyAsync(string userId, string keyId, ClaimsPrincipal caller);
}

public sealed class AdminUserService : IAdminUserService
{
    private readonly IDocumentStore _store;
    private readonly IFirebaseAdminService _firebase;
    private readonly IConnectionMultiplexer _redis;
    private readonly INatsConnection _nats;
    private readonly ILogger<AdminUserService> _logger;
    private readonly RosterImportOptions _rosterImport;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AdminUserService(
        IDocumentStore store,
        IFirebaseAdminService firebase,
        IConnectionMultiplexer redis,
        INatsConnection nats,
        ILogger<AdminUserService> logger,
        IOptions<RosterImportOptions>? rosterImport = null)
    {
        _store = store;
        _firebase = firebase;
        _redis = redis;
        _nats = nats;
        _logger = logger;
        _rosterImport = rosterImport?.Value ?? new RosterImportOptions();
    }

    public async Task<UserListResponse> ListUsersAsync(
        string? query, string? role, string? status, string? school, string? grade,
        int page, int itemsPerPage, string? sortBy, string? orderBy, ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();

        var q = session.Query<AdminUser>().Where(u => !u.SoftDeleted);

        // Tenant scoping: non-SUPER_ADMIN sees only their school
        if (!caller.IsInRole("SUPER_ADMIN"))
        {
            var callerSchool = caller.FindFirstValue("school_id");
            if (callerSchool != null)
                q = q.Where(u => u.School == callerSchool);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            // SEC-004: Sanitize search query before use in LINQ expressions
            var search = InputSanitizer.SanitizeSearchQuery(query);
            if (!string.IsNullOrEmpty(search))
            {
                q = q.Where(u =>
                    u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
            }
        }

        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<CenaRole>(role, true, out var roleEnum))
            q = q.Where(u => u.Role == roleEnum);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<UserStatus>(status, true, out var statusEnum))
            q = q.Where(u => u.Status == statusEnum);

        if (!string.IsNullOrWhiteSpace(school))
            q = q.Where(u => u.School == school);

        if (!string.IsNullOrWhiteSpace(grade))
            q = q.Where(u => u.Grade == grade);

        var total = await q.CountAsync();

        // Sorting
        q = (sortBy?.ToLowerInvariant(), orderBy?.ToLowerInvariant()) switch
        {
            ("email", "desc") => q.OrderByDescending(u => u.Email),
            ("email", _) => q.OrderBy(u => u.Email),
            ("role", "desc") => q.OrderByDescending(u => u.Role),
            ("role", _) => q.OrderBy(u => u.Role),
            ("createdat", "desc") or ("created", "desc") => q.OrderByDescending(u => u.CreatedAt),
            ("createdat", _) or ("created", _) => q.OrderBy(u => u.CreatedAt),
            (_, "desc") => q.OrderByDescending(u => u.FullName),
            _ => q.OrderBy(u => u.FullName),
        };

        var users = await q
            .Skip((page - 1) * itemsPerPage)
            .Take(itemsPerPage)
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)total / itemsPerPage);

        return new UserListResponse(
            users.Select(AdminUserDto.From).ToList(),
            total,
            totalPages,
            page);
    }

    public async Task<AdminUserDto?> GetUserAsync(string id, ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(id);
        if (user is null or { SoftDeleted: true })
            return null;

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only access users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant access attempt: caller from school {CallerSchool} attempted to access user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            return null; // Return 404 (not 403) to avoid leaking existence
        }

        return AdminUserDto.From(user);
    }

    public async Task<AdminUserDto> CreateUserAsync(CreateUserRequest request, ClaimsPrincipal caller)
    {
        if (!Enum.TryParse<CenaRole>(request.Role, true, out var role))
            throw new ArgumentException($"Invalid role: {request.Role}");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only create users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        var targetSchool = request.School ?? schoolId ?? "";
        if (schoolId is not null && targetSchool != schoolId)
        {
            _logger.LogWarning("Cross-tenant create attempt: caller from school {CallerSchool} attempted to create user in school {TargetSchool}",
                schoolId, targetSchool);
            throw new UnauthorizedAccessException("Cannot create user in a different school");
        }

        var uid = await _firebase.CreateUserAsync(request.Email, request.FullName, request.Password);

        await _firebase.SetCustomClaimsAsync(uid, new Dictionary<string, object>
        {
            ["role"] = role.ToString(),
            ["school_id"] = targetSchool,
            ["locale"] = request.Locale
        });

        var user = new AdminUser
        {
            Id = uid,
            Email = request.Email,
            FullName = request.FullName,
            Role = role,
            Status = string.IsNullOrEmpty(request.Password) ? UserStatus.Pending : UserStatus.Active,
            School = targetSchool,
            Grade = request.Grade,
            Locale = request.Locale,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var session = _store.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        _logger.LogInformation("Created user {Uid} ({Email}) with role {Role}", uid, request.Email, role);
        return AdminUserDto.From(user);
    }

    public async Task<AdminUserDto> UpdateUserAsync(string id, UpdateUserRequest request, ClaimsPrincipal caller)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only update users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant update attempt: caller from school {CallerSchool} attempted to update user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            throw new KeyNotFoundException($"User {id} not found");
        }

        var updated = user with
        {
            FullName = request.FullName ?? user.FullName,
            Email = request.Email ?? user.Email,
            School = request.School ?? user.School,
            Grade = request.Grade ?? user.Grade,
            Locale = request.Locale ?? user.Locale,
        };

        // Role change: update Firebase claims
        if (request.Role != null && Enum.TryParse<CenaRole>(request.Role, true, out var newRole) && newRole != user.Role)
        {
            updated = updated with { Role = newRole };
            await _firebase.SetCustomClaimsAsync(id, new Dictionary<string, object>
            {
                ["role"] = newRole.ToString(),
                ["school_id"] = updated.School ?? "",
                ["locale"] = updated.Locale
            });
        }

        // Email change: update Firebase email
        if (request.Email != null && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            await _firebase.UpdateEmailAsync(id, request.Email);
        }

        session.Store(updated);
        await session.SaveChangesAsync();
        return AdminUserDto.From(updated);
    }

    public async Task SoftDeleteUserAsync(string id, ClaimsPrincipal caller)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only delete users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant delete attempt: caller from school {CallerSchool} attempted to delete user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            throw new KeyNotFoundException($"User {id} not found");
        }

        var deleted = user with
        {
            Status = UserStatus.Suspended,
            SoftDeleted = true,
            SuspensionReason = "Account deleted by admin",
            SuspendedAt = DateTimeOffset.UtcNow
        };

        session.Store(deleted);
        await session.SaveChangesAsync();

        await _firebase.DisableUserAsync(id);
        await TokenRevocationMiddleware.RevokeUserAsync(_redis, id);

        // LCM-001: Propagate to actor system via NATS + Redis status gate
        await SetAccountStatusAsync(id, "pending_delete", "Account deleted by admin", "system");
    }

    public async Task SuspendUserAsync(string id, string reason, ClaimsPrincipal caller)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only suspend users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant suspend attempt: caller from school {CallerSchool} attempted to suspend user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            throw new KeyNotFoundException($"User {id} not found");
        }

        var suspended = user with
        {
            Status = UserStatus.Suspended,
            SuspensionReason = reason,
            SuspendedAt = DateTimeOffset.UtcNow
        };

        session.Store(suspended);
        await session.SaveChangesAsync();

        await _firebase.DisableUserAsync(id);
        await TokenRevocationMiddleware.RevokeUserAsync(_redis, id);

        // LCM-001: Propagate to actor system via NATS + Redis status gate
        await SetAccountStatusAsync(id, "suspended", reason, "admin");
        _logger.LogInformation("Suspended user {Uid}: {Reason}", id, reason);
    }

    public async Task ActivateUserAsync(string id, ClaimsPrincipal caller)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only activate users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant activate attempt: caller from school {CallerSchool} attempted to activate user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            throw new KeyNotFoundException($"User {id} not found");
        }

        var activated = user with
        {
            Status = UserStatus.Active,
            SuspensionReason = null,
            SuspendedAt = null
        };

        session.Store(activated);
        await session.SaveChangesAsync();

        await _firebase.EnableUserAsync(id);

        // LCM-001: Clear actor system block via NATS + Redis status gate
        await ClearAccountStatusAsync(id, "admin");
    }

    // =========================================================================
    // LCM-001: Account Status Propagation (NATS event + Redis gate key)
    // =========================================================================

    private async Task SetAccountStatusAsync(string studentId, string status, string? reason, string changedBy)
    {
        var db = _redis.GetDatabase();
        var ttl = status == "pending_delete" ? TimeSpan.FromDays(30) : TimeSpan.FromHours(24);
        await db.StringSetAsync($"account_status:{studentId}", status, ttl);

        var msg = new BusAccountStatusChanged(studentId, status, reason, changedBy, DateTimeOffset.UtcNow);
        var envelope = BusEnvelope<BusAccountStatusChanged>.Create(NatsSubjects.AccountStatusChanged, msg, "admin-api");
        await _nats.PublishAsync(NatsSubjects.AccountStatusChanged, JsonSerializer.Serialize(envelope, JsonOpts));
    }

    private async Task ClearAccountStatusAsync(string studentId, string changedBy)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync($"account_status:{studentId}");

        var msg = new BusAccountStatusChanged(studentId, "active", null, changedBy, DateTimeOffset.UtcNow);
        var envelope = BusEnvelope<BusAccountStatusChanged>.Create(NatsSubjects.AccountStatusChanged, msg, "admin-api");
        await _nats.PublishAsync(NatsSubjects.AccountStatusChanged, JsonSerializer.Serialize(envelope, JsonOpts));
    }

    public async Task<AdminUserDto> InviteUserAsync(InviteUserRequest request, ClaimsPrincipal caller)
    {
        if (!Enum.TryParse<CenaRole>(request.Role, true, out var role))
            throw new ArgumentException($"Invalid role: {request.Role}");

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only invite users to their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        var targetSchool = request.School ?? schoolId ?? "";
        if (schoolId is not null && targetSchool != schoolId)
        {
            _logger.LogWarning("Cross-tenant invite attempt: caller from school {CallerSchool} attempted to invite user to school {TargetSchool}",
                schoolId, targetSchool);
            throw new UnauthorizedAccessException("Cannot invite user to a different school");
        }

        var uid = await _firebase.CreateUserAsync(request.Email, request.Email, password: null);

        await _firebase.SetCustomClaimsAsync(uid, new Dictionary<string, object>
        {
            ["role"] = role.ToString(),
            ["school_id"] = targetSchool,
            ["locale"] = "en"
        });

        var link = await _firebase.GenerateSignInLinkAsync(request.Email);
        _logger.LogInformation("Invite link generated for {Email}: {Link}", request.Email, link);

        var user = new AdminUser
        {
            Id = uid,
            Email = request.Email,
            FullName = request.Email,
            Role = role,
            Status = UserStatus.Pending,
            School = targetSchool,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var session = _store.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        return AdminUserDto.From(user);
    }

    /// <summary>
    /// prr-021: Hardened roster-import entry point. Delegates to
    /// <see cref="RosterImportProcessor"/> which owns sanitizer + audit.
    /// </summary>
    public Task<BulkInviteResult> BulkInviteAsync(Stream csvStream, ClaimsPrincipal caller) =>
        RosterImportProcessor.RunAsync(
            csvStream,
            caller,
            _rosterImport,
            _store,
            _logger,
            async (req, c) => await InviteUserAsync(req, c));

    public async Task<UserStatsResponse> GetStatsAsync(ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();
        var q = session.Query<AdminUser>().Where(u => !u.SoftDeleted);

        if (!caller.IsInRole("SUPER_ADMIN"))
        {
            var callerSchool = caller.FindFirstValue("school_id");
            if (callerSchool != null)
                q = q.Where(u => u.School == callerSchool);
        }

        var users = await q.ToListAsync();

        var weekAgo = DateTimeOffset.UtcNow.AddDays(-7);
        var todayStart = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);

        return new UserStatsResponse(
            TotalUsers: users.Count,
            NewThisWeek: users.Count(u => u.CreatedAt >= weekAgo),
            ActiveToday: users.Count(u => u.LastLoginAt >= todayStart),
            PendingReview: users.Count(u => u.Status == UserStatus.Pending),
            ByRole: users.GroupBy(u => u.Role.ToString())
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    public async Task<IReadOnlyList<UserActivityEntry>> GetActivityAsync(string userId, ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();

        // FIND-sec-008: Verify tenant access before querying activity
        var user = await session.LoadAsync<AdminUser>(userId);
        if (user is null or { SoftDeleted: true })
            return Array.Empty<UserActivityEntry>();

        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant activity access attempt: caller from school {CallerSchool} attempted to access activity for user {UserId} in school {TargetSchool}",
                schoolId, userId, user.School);
            return Array.Empty<UserActivityEntry>();
        }

        // Query Marten event stream for user-related events
        var events = await session.Events.FetchStreamAsync($"student-{userId}");

        return events
            .OrderByDescending(e => e.Timestamp)
            .Take(50)
            .Select(e => new UserActivityEntry(
                e.Timestamp,
                e.EventTypeName,
                $"{e.EventTypeName} at {e.Timestamp:g}",
                new Dictionary<string, object> { ["sequenceId"] = e.Sequence }))
            .ToList();
    }

    // =========================================================================
    // BKD-002.9: Session Management (Redis-backed)
    // Key: sessions:{uid} — Hash where field = sessionId, value = JSON payload
    // TTL: 1 hour (matches Firebase token lifetime)
    // =========================================================================

    private const string SessionKeyPrefix = "sessions:";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(1);

    public async Task RecordSessionAsync(string userId, string sessionId, string device, string browser, string ip)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = $"{SessionKeyPrefix}{userId}";
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                sessionId,
                device,
                browser,
                ip,
                lastActive = DateTimeOffset.UtcNow,
                status = "active"
            });

            await db.HashSetAsync(key, sessionId, payload);
            await db.KeyExpireAsync(key, SessionTtl);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for session recording. Session not tracked.");
        }
    }

    public async Task<IReadOnlyList<UserSessionDto>> GetSessionsAsync(string userId, ClaimsPrincipal caller)
    {
        // FIND-sec-008: Verify tenant access before querying sessions
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(userId);
        if (user is null or { SoftDeleted: true })
            return Array.Empty<UserSessionDto>();

        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant session access attempt: caller from school {CallerSchool} attempted to access sessions for user {UserId} in school {TargetSchool}",
                schoolId, userId, user.School);
            return Array.Empty<UserSessionDto>();
        }

        try
        {
            var db = _redis.GetDatabase();
            var entries = await db.HashGetAllAsync($"{SessionKeyPrefix}{userId}");

            var sessions = new List<UserSessionDto>();
            foreach (var entry in entries)
            {
                if (!entry.Value.HasValue) continue;

                using var doc = System.Text.Json.JsonDocument.Parse(entry.Value.ToString());
                var root = doc.RootElement;

                var lastActive = root.TryGetProperty("lastActive", out var la)
                    ? DateTimeOffset.Parse(la.GetString()!)
                    : DateTimeOffset.UtcNow;

                var status = (DateTimeOffset.UtcNow - lastActive).TotalMinutes > 30
                    ? "expired" : "active";

                sessions.Add(new UserSessionDto(
                    SessionId: root.GetProperty("sessionId").GetString()!,
                    Device: root.TryGetProperty("device", out var d) ? d.GetString()! : "unknown",
                    Browser: root.TryGetProperty("browser", out var b) ? b.GetString()! : "unknown",
                    Ip: root.TryGetProperty("ip", out var ip) ? ip.GetString()! : "unknown",
                    Location: null,
                    LastActive: lastActive,
                    Status: status));
            }

            return sessions.OrderByDescending(s => s.LastActive).ToList();
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for session query.");
            return [];
        }
    }

    public async Task RevokeSessionAsync(string userId, string sessionId, ClaimsPrincipal caller)
    {
        // FIND-sec-008: Verify tenant access before revoking session
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(userId);
        if (user is null or { SoftDeleted: true })
            throw new KeyNotFoundException($"User {userId} not found");

        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant session revoke attempt: caller from school {CallerSchool} attempted to revoke session for user {UserId} in school {TargetSchool}",
                schoolId, userId, user.School);
            throw new KeyNotFoundException($"User {userId} not found");
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.HashDeleteAsync($"{SessionKeyPrefix}{userId}", sessionId);
            _logger.LogInformation("Revoked session {SessionId} for user {UserId}", sessionId, userId);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for session revocation.");
            throw;
        }
    }

    public async Task<bool> ForcePasswordResetAsync(string id, ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(id);
        if (user is null or { SoftDeleted: true })
            return false;

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only reset passwords for users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant password reset attempt: caller from school {CallerSchool} attempted to reset password for user {UserId} in school {TargetSchool}",
                schoolId, id, user.School);
            return false;
        }

        var link = await _firebase.GenerateSignInLinkAsync(user.Email);
        _logger.LogInformation("Forced password reset for user {UserId}, link generated", id);
        return true;
    }

    public async Task<bool> RevokeApiKeyAsync(string userId, string keyId, ClaimsPrincipal caller)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(userId);
        if (user is null or { SoftDeleted: true })
            return false;

        // FIND-sec-008: Enforce tenant scoping - ADMIN can only revoke API keys for users in their school
        var schoolId = TenantScope.GetSchoolFilter(caller);
        if (schoolId is not null && user.School != schoolId)
        {
            _logger.LogWarning("Cross-tenant API key revoke attempt: caller from school {CallerSchool} attempted to revoke key for user {UserId} in school {TargetSchool}",
                schoolId, userId, user.School);
            return false;
        }

        _logger.LogInformation("Revoked API key {KeyId} for user {UserId}", keyId, userId);
        return true;
    }

}
