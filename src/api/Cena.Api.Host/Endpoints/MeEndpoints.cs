// =============================================================================
// Cena Platform -- Me/Profile REST Endpoints (STB-00, STB-00b)
// Student-facing bootstrap and profile endpoints
// =============================================================================

using System.Security.Claims;
using Cena.Api.Contracts.Me;
using Cena.Actors.Events;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

public static class MeEndpoints
{
    // Marker type for logger (static classes can't be type arguments)
    private sealed class MeLoggerMarker { }

    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me")
            .WithTags("Me")
            .RequireAuthorization();

        // STB-00 Phase 1
        group.MapGet("", GetBootstrap).WithName("GetMe");
        group.MapGet("/profile", GetProfile).WithName("GetMeProfile");
        group.MapPatch("/profile", UpdateProfile).WithName("UpdateMeProfile");
        group.MapPost("/onboarding", SubmitOnboarding).WithName("SubmitOnboarding");

        // STB-00b: Settings
        group.MapGet("/settings", GetSettings).WithName("GetSettings");
        group.MapPatch("/settings", UpdateSettings).WithName("UpdateSettings");

        // STB-00b: Home Layout
        group.MapPut("/preferences/home-layout", UpdateHomeLayout).WithName("UpdateHomeLayout");

        // STB-00b: Devices
        group.MapGet("/devices", GetDevices).WithName("GetDevices");
        group.MapPost("/devices/{id}/revoke", RevokeDevice).WithName("RevokeDevice");

        // STB-00b: Share Tokens
        group.MapPost("/share-tokens", CreateShareToken).WithName("CreateShareToken");

        return app;
    }

    // ---- STB-00 Phase 1 endpoints ----

    // GET /api/me — bootstrap payload
    private static async Task<IResult> GetBootstrap(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        var dto = new MeBootstrapDto(
            StudentId: profile.StudentId,
            DisplayName: profile.DisplayName ?? profile.FullName ?? "Student",
            Role: profile.Role ?? "student",
            Locale: profile.Locale ?? "en",
            OnboardedAt: profile.OnboardedAt,
            Subjects: profile.Subjects,
            Level: CalculateLevel(profile.TotalXp),
            StreakDays: profile.CurrentStreak,
            AvatarUrl: null); // TODO: Avatar storage in STB-00b

        return Results.Ok(dto);
    }

    // GET /api/me/profile — full profile view
    private static async Task<IResult> GetProfile(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        var dto = new ProfileDto(
            StudentId: profile.StudentId,
            DisplayName: profile.DisplayName ?? profile.FullName ?? "Student",
            Email: ctx.User.FindFirst(ClaimTypes.Email)?.Value ?? "",
            AvatarUrl: null, // TODO: Avatar storage in STB-00b
            Bio: profile.Bio,
            FavoriteSubjects: profile.Subjects,
            Visibility: profile.Visibility);

        return Results.Ok(dto);
    }

    // PATCH /api/me/profile — update profile
    private static async Task<IResult> UpdateProfile(
        HttpContext ctx,
        IDocumentStore store,
        ProfilePatchDto patch)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
            return Results.NotFound();

        // FIND-data-007b: Emit ProfileUpdated event instead of direct snapshot modification.
        // The inline projection will apply this event to the snapshot on SaveChangesAsync.
        var profileUpdatedEvent = new ProfileUpdated_V1(
            StudentId: studentId,
            DisplayName: patch.DisplayName,
            Bio: patch.Bio,
            Subjects: patch.FavoriteSubjects,
            Visibility: patch.Visibility,
            UpdatedAt: DateTimeOffset.UtcNow);

        session.Events.Append(studentId, profileUpdatedEvent);
        await session.SaveChangesAsync();

        return Results.NoContent();
    }

    // POST /api/me/onboarding — submit onboarding
    private static async Task<IResult> SubmitOnboarding(
        HttpContext ctx,
        IDocumentStore store,
        OnboardingRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);

        if (profile is null)
        {
            // Create new profile for this student
            profile = new StudentProfileSnapshot
            {
                StudentId = studentId,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        // Idempotency: if already onboarded, return success without re-appending event
        if (profile.OnboardedAt.HasValue)
        {
            return Results.Ok(new OnboardingResponse(Success: true, RedirectTo: "/home"));
        }

        // Create and append onboarding event
        var onboardingEvent = new OnboardingCompleted_V1(
            StudentId: studentId,
            Role: request.Role,
            Locale: request.Locale,
            Subjects: request.Subjects,
            DailyTimeGoalMinutes: request.DailyTimeGoalMinutes,
            CompletedAt: DateTimeOffset.UtcNow);

        session.Events.Append(studentId, onboardingEvent);

        // FIND-data-007b: Removed profile.Apply and session.Store(profile).
        // StudentProfileSnapshot is an INLINE projection — Marten auto-rebuilds
        // from events on SaveChangesAsync(). Manual Store() races with daemon.

        await session.SaveChangesAsync();

        return Results.Ok(new OnboardingResponse(Success: true, RedirectTo: "/home"));
    }

    // ---- STB-00b: Settings endpoints ----

    // GET /api/me/settings
    private static async Task<IResult> GetSettings(
        HttpContext ctx,
        IDocumentStore store,
        ILogger<MeLoggerMarker> logger)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var prefs = await session.LoadAsync<StudentPreferencesDocument>(studentId);

        if (prefs is null)
        {
            // Create default preferences on first access
            prefs = CreateDefaultPreferences(studentId);
            session.Store(prefs);
            await session.SaveChangesAsync();
        }

        // Placeholder for FERPA logging (deferred per spec)
        logger.LogInformation("Student {StudentId} accessed settings", studentId);

        var dto = MapToSettingsDto(prefs);
        return Results.Ok(dto);
    }

    // PATCH /api/me/settings
    private static async Task<IResult> UpdateSettings(
        HttpContext ctx,
        IDocumentStore store,
        SettingsPatchDto patch)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var prefs = await session.LoadAsync<StudentPreferencesDocument>(studentId);

        if (prefs is null)
        {
            prefs = CreateDefaultPreferences(studentId);
        }

        // Apply patches
        if (patch.Appearance is not null)
        {
            prefs.Theme = patch.Appearance.Theme;
            prefs.Language = patch.Appearance.Language;
            prefs.ReducedMotion = patch.Appearance.ReducedMotion;
            prefs.HighContrast = patch.Appearance.HighContrast;
        }

        if (patch.Notifications is not null)
        {
            prefs.EmailNotifications = patch.Notifications.EmailNotifications;
            prefs.PushNotifications = patch.Notifications.PushNotifications;
            prefs.DailyReminder = patch.Notifications.DailyReminder;
            prefs.DailyReminderTime = patch.Notifications.DailyReminderTime;
            prefs.WeeklyProgress = patch.Notifications.WeeklyProgress;
            prefs.StreakAlerts = patch.Notifications.StreakAlerts;
            prefs.NewContentAlerts = patch.Notifications.NewContentAlerts;
        }

        if (patch.Privacy is not null)
        {
            prefs.ProfileVisibility = patch.Privacy.ProfileVisibility;
            prefs.ShowProgressToClass = patch.Privacy.ShowProgressToClass;
            prefs.AllowPeerComparison = patch.Privacy.AllowPeerComparison;
            prefs.ShareAnalytics = patch.Privacy.ShareAnalytics;
        }

        if (patch.Learning is not null)
        {
            prefs.AutoAdvance = patch.Learning.AutoAdvance;
            prefs.ShowHintsByDefault = patch.Learning.ShowHintsByDefault;
            prefs.SoundEffects = patch.Learning.SoundEffects;
            prefs.TargetSessionMinutes = patch.Learning.TargetSessionMinutes;
            prefs.DifficultyPreference = patch.Learning.DifficultyPreference;
        }

        if (patch.HomeLayout is not null)
        {
            prefs.HomeLayout.WidgetOrder = patch.HomeLayout.WidgetOrder;
            prefs.HomeLayout.HiddenWidgets = patch.HomeLayout.HiddenWidgets;
            prefs.HomeLayout.CompactMode = patch.HomeLayout.CompactMode;
        }

        prefs.UpdatedAt = DateTime.UtcNow;
        session.Store(prefs);
        await session.SaveChangesAsync();

        return Results.NoContent();
    }

    // ---- STB-00b: Home Layout ----

    // PUT /api/me/preferences/home-layout
    private static async Task<IResult> UpdateHomeLayout(
        HttpContext ctx,
        IDocumentStore store,
        HomeLayoutPatchDto request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var prefs = await session.LoadAsync<StudentPreferencesDocument>(studentId);

        if (prefs is null)
        {
            prefs = CreateDefaultPreferences(studentId);
        }

        if (request.WidgetOrder is not null)
            prefs.HomeLayout.WidgetOrder = request.WidgetOrder;
        if (request.HiddenWidgets is not null)
            prefs.HomeLayout.HiddenWidgets = request.HiddenWidgets;
        if (request.CompactMode.HasValue)
            prefs.HomeLayout.CompactMode = request.CompactMode.Value;

        prefs.UpdatedAt = DateTime.UtcNow;
        session.Store(prefs);
        await session.SaveChangesAsync();

        return Results.NoContent();
    }

    // ---- STB-00b: Devices ----

    // GET /api/me/devices
    private static async Task<IResult> GetDevices(
        HttpContext ctx,
        IDocumentStore store)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.QuerySession();
        var devices = await session.Query<DeviceSessionDocument>()
            .Where(d => d.StudentId == studentId && !d.IsRevoked)
            .ToListAsync();

        // Get current device identifier (simplified - use session ID hash)
        var currentSessionId = ctx.Session.Id ?? ctx.TraceIdentifier;
        var currentDeviceId = $"device-{studentId}-{currentSessionId.GetHashCode():x8}";

        var dtos = devices.Select(d => new DeviceDto(
            Id: d.Id,
            Platform: d.Platform,
            DeviceName: d.DeviceName,
            DeviceModel: d.DeviceModel,
            FirstSeenAt: d.FirstSeenAt,
            LastSeenAt: d.LastSeenAt,
            IsCurrentDevice: d.Id == currentDeviceId)).ToArray();

        return Results.Ok(dtos);
    }

    // POST /api/me/devices/{id}/revoke
    private static async Task<IResult> RevokeDevice(
        HttpContext ctx,
        IDocumentStore store,
        string id)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        await using var session = store.LightweightSession();
        var device = await session.LoadAsync<DeviceSessionDocument>(id);

        // Idempotent: if device doesn't exist or is already revoked, still return 200
        if (device is null || device.StudentId != studentId)
        {
            return Results.Ok(new { Revoked = true, Id = id });
        }

        if (!device.IsRevoked)
        {
            device.IsRevoked = true;
            device.RevokedAt = DateTime.UtcNow;
            session.Store(device);
            await session.SaveChangesAsync();
        }

        return Results.Ok(new { Revoked = true, Id = id });
    }

    // ---- STB-00b: Share Tokens ----

    // POST /api/me/share-tokens
    private static async Task<IResult> CreateShareToken(
        HttpContext ctx,
        IDocumentStore store,
        ShareTokenRequest request)
    {
        var studentId = GetStudentId(ctx.User);
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        // Validate request
        if (string.IsNullOrEmpty(request.Audience) || request.Scopes.Length == 0)
        {
            return Results.BadRequest(new { Error = "Audience and at least one scope are required" });
        }

        await using var session = store.LightweightSession();

        // Generate token (in practice, use cryptographically secure random)
        var tokenValue = $"cena_share_{studentId}_{Guid.NewGuid():N}";
        var expiresAt = DateTime.UtcNow.AddDays(request.ExpiresInDays > 0 ? request.ExpiresInDays : 7);

        var shareToken = new ShareTokenDocument
        {
            Id = $"share_{studentId}_{Guid.NewGuid():N}",
            Token = tokenValue,
            StudentId = studentId,
            Audience = request.Audience,
            Scopes = request.Scopes,
            ExpiresAt = expiresAt
        };

        session.Store(shareToken);
        await session.SaveChangesAsync();

        var dto = new ShareTokenResponse(
            Token: tokenValue,
            Url: $"/share/{tokenValue}",
            ExpiresAt: expiresAt);

        return Results.Ok(dto);
    }

    // ---- Helper methods ----

    private static string? GetStudentId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static int CalculateLevel(int totalXp)
    {
        // Simple level calculation: 100 XP per level
        return Math.Max(1, totalXp / 100);
    }

    private static StudentPreferencesDocument CreateDefaultPreferences(string studentId)
    {
        return new StudentPreferencesDocument
        {
            Id = studentId,
            StudentId = studentId,
            Theme = "system",
            Language = "en",
            ReducedMotion = false,
            HighContrast = false,
            EmailNotifications = true,
            PushNotifications = true,
            DailyReminder = true,
            DailyReminderTime = TimeSpan.FromHours(9),
            WeeklyProgress = true,
            StreakAlerts = true,
            NewContentAlerts = true,
            ProfileVisibility = "class-only",
            ShowProgressToClass = true,
            AllowPeerComparison = true,
            ShareAnalytics = false,
            AutoAdvance = false,
            ShowHintsByDefault = true,
            SoundEffects = true,
            TargetSessionMinutes = 15,
            DifficultyPreference = "adaptive",
            HomeLayout = new HomeLayout
            {
                WidgetOrder = new[] { "streak", "progress", "recommended", "achievements", "activity" },
                HiddenWidgets = Array.Empty<string>(),
                CompactMode = false
            },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static SettingsDto MapToSettingsDto(StudentPreferencesDocument prefs)
    {
        return new SettingsDto(
            Appearance: new AppearanceSettings(
                Theme: prefs.Theme,
                Language: prefs.Language,
                ReducedMotion: prefs.ReducedMotion,
                HighContrast: prefs.HighContrast),
            Notifications: new NotificationSettings(
                EmailNotifications: prefs.EmailNotifications,
                PushNotifications: prefs.PushNotifications,
                DailyReminder: prefs.DailyReminder,
                DailyReminderTime: prefs.DailyReminderTime,
                WeeklyProgress: prefs.WeeklyProgress,
                StreakAlerts: prefs.StreakAlerts,
                NewContentAlerts: prefs.NewContentAlerts),
            Privacy: new PrivacySettings(
                ProfileVisibility: prefs.ProfileVisibility,
                ShowProgressToClass: prefs.ShowProgressToClass,
                AllowPeerComparison: prefs.AllowPeerComparison,
                ShareAnalytics: prefs.ShareAnalytics),
            Learning: new LearningSettings(
                AutoAdvance: prefs.AutoAdvance,
                ShowHintsByDefault: prefs.ShowHintsByDefault,
                SoundEffects: prefs.SoundEffects,
                TargetSessionMinutes: prefs.TargetSessionMinutes,
                DifficultyPreference: prefs.DifficultyPreference),
            HomeLayout: new HomeLayoutDto(
                WidgetOrder: prefs.HomeLayout.WidgetOrder,
                HiddenWidgets: prefs.HomeLayout.HiddenWidgets,
                CompactMode: prefs.HomeLayout.CompactMode));
    }
}
