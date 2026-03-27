// =============================================================================
// Cena Platform -- Admin User Management Service
// BKD-002: Business logic for user CRUD, suspension, invites
// =============================================================================

using System.Globalization;
using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Firebase;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IAdminUserService
{
    Task<UserListResponse> ListUsersAsync(
        string? query, string? role, string? status, string? school, string? grade,
        int page, int itemsPerPage, string? sortBy, string? orderBy, ClaimsPrincipal caller);
    Task<AdminUserDto?> GetUserAsync(string id);
    Task<AdminUserDto> CreateUserAsync(CreateUserRequest request);
    Task<AdminUserDto> UpdateUserAsync(string id, UpdateUserRequest request);
    Task SoftDeleteUserAsync(string id);
    Task SuspendUserAsync(string id, string reason);
    Task ActivateUserAsync(string id);
    Task<AdminUserDto> InviteUserAsync(InviteUserRequest request);
    Task<BulkInviteResult> BulkInviteAsync(Stream csvStream);
    Task<UserStatsResponse> GetStatsAsync(ClaimsPrincipal caller);
    Task<IReadOnlyList<UserActivityEntry>> GetActivityAsync(string userId);
    Task<IReadOnlyList<UserSessionDto>> GetSessionsAsync(string userId);
    Task RevokeSessionAsync(string userId, string sessionId);
    Task RecordSessionAsync(string userId, string sessionId, string device, string browser, string ip);
    Task<bool> ForcePasswordResetAsync(string id);
    Task<bool> RevokeApiKeyAsync(string userId, string keyId);
}

public sealed class AdminUserService : IAdminUserService
{
    private readonly IDocumentStore _store;
    private readonly IFirebaseAdminService _firebase;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AdminUserService> _logger;

    public AdminUserService(
        IDocumentStore store,
        IFirebaseAdminService firebase,
        IConnectionMultiplexer redis,
        ILogger<AdminUserService> logger)
    {
        _store = store;
        _firebase = firebase;
        _redis = redis;
        _logger = logger;
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
            var search = query.Trim();
            q = q.Where(u =>
                u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
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

    public async Task<AdminUserDto?> GetUserAsync(string id)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(id);
        return user is { SoftDeleted: false } ? AdminUserDto.From(user) : null;
    }

    public async Task<AdminUserDto> CreateUserAsync(CreateUserRequest request)
    {
        if (!Enum.TryParse<CenaRole>(request.Role, true, out var role))
            throw new ArgumentException($"Invalid role: {request.Role}");

        var uid = await _firebase.CreateUserAsync(request.Email, request.FullName, request.Password);

        await _firebase.SetCustomClaimsAsync(uid, new Dictionary<string, object>
        {
            ["role"] = role.ToString(),
            ["school_id"] = request.School ?? "",
            ["locale"] = request.Locale
        });

        var user = new AdminUser
        {
            Id = uid,
            Email = request.Email,
            FullName = request.FullName,
            Role = role,
            Status = string.IsNullOrEmpty(request.Password) ? UserStatus.Pending : UserStatus.Active,
            School = request.School,
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

    public async Task<AdminUserDto> UpdateUserAsync(string id, UpdateUserRequest request)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

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

    public async Task SoftDeleteUserAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

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
    }

    public async Task SuspendUserAsync(string id, string reason)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

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
        _logger.LogInformation("Suspended user {Uid}: {Reason}", id, reason);
    }

    public async Task ActivateUserAsync(string id)
    {
        await using var session = _store.LightweightSession();
        var user = await session.LoadAsync<AdminUser>(id)
            ?? throw new KeyNotFoundException($"User {id} not found");

        var activated = user with
        {
            Status = UserStatus.Active,
            SuspensionReason = null,
            SuspendedAt = null
        };

        session.Store(activated);
        await session.SaveChangesAsync();

        await _firebase.EnableUserAsync(id);
    }

    public async Task<AdminUserDto> InviteUserAsync(InviteUserRequest request)
    {
        if (!Enum.TryParse<CenaRole>(request.Role, true, out var role))
            throw new ArgumentException($"Invalid role: {request.Role}");

        var uid = await _firebase.CreateUserAsync(request.Email, request.Email, password: null);

        await _firebase.SetCustomClaimsAsync(uid, new Dictionary<string, object>
        {
            ["role"] = role.ToString(),
            ["school_id"] = request.School ?? "",
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
            School = request.School,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await using var session = _store.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        return AdminUserDto.From(user);
    }

    public async Task<BulkInviteResult> BulkInviteAsync(Stream csvStream)
    {
        var created = 0;
        var failed = new List<BulkInviteFailure>();

        using var reader = new StreamReader(csvStream);
        var header = await reader.ReadLineAsync(); // skip header

        while (await reader.ReadLineAsync() is { } line)
        {
            var parts = line.Split(',');
            if (parts.Length < 3)
            {
                failed.Add(new BulkInviteFailure(line, "Invalid CSV row — expected: name,email,role"));
                continue;
            }

            var name = parts[0].Trim().Trim('"');
            var email = parts[1].Trim().Trim('"');
            var role = parts[2].Trim().Trim('"');

            try
            {
                await InviteUserAsync(new InviteUserRequest(email, role, null));
                created++;
            }
            catch (Exception ex)
            {
                failed.Add(new BulkInviteFailure(email, ex.Message));
            }
        }

        return new BulkInviteResult(created, failed);
    }

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
        var todayStart = DateTimeOffset.UtcNow.Date;

        return new UserStatsResponse(
            TotalUsers: users.Count,
            NewThisWeek: users.Count(u => u.CreatedAt >= weekAgo),
            ActiveToday: users.Count(u => u.LastLoginAt >= todayStart),
            PendingReview: users.Count(u => u.Status == UserStatus.Pending),
            ByRole: users.GroupBy(u => u.Role.ToString())
                .ToDictionary(g => g.Key, g => g.Count()));
    }

    public async Task<IReadOnlyList<UserActivityEntry>> GetActivityAsync(string userId)
    {
        await using var session = _store.QuerySession();

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

    public async Task<IReadOnlyList<UserSessionDto>> GetSessionsAsync(string userId)
    {
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

    public async Task RevokeSessionAsync(string userId, string sessionId)
    {
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

    public async Task<bool> ForcePasswordResetAsync(string id)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(id);
        if (user is null or { SoftDeleted: true })
            return false;

        var link = await _firebase.GenerateSignInLinkAsync(user.Email);
        _logger.LogInformation("Forced password reset for user {UserId}, link generated", id);
        return true;
    }

    public async Task<bool> RevokeApiKeyAsync(string userId, string keyId)
    {
        await using var session = _store.QuerySession();
        var user = await session.LoadAsync<AdminUser>(userId);
        if (user is null or { SoftDeleted: true })
            return false;

        _logger.LogInformation("Revoked API key {KeyId} for user {UserId}", keyId, userId);
        return true;
    }

}
