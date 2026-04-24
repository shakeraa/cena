// =============================================================================
// Cena Platform -- Firebase Custom Claims Seeder
// Syncs Firebase custom claims for admin-level demo users on startup.
// Ensures role, school_id, locale, plan are always set correctly.
// =============================================================================

using Cena.Infrastructure.Documents;
using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

public static class FirebaseClaimsSeeder
{
    private static readonly (string Email, CenaRole Role, string? School, string Locale, string Plan)[] AdminUsers =
    [
        ("shaker.abuayoub@gmail.com", CenaRole.SUPER_ADMIN, null, "he", "premium"),
        ("admin@cena-demo.edu", CenaRole.ADMIN, "school-haifa-01", "he", "free"),
        ("admin2@cena-demo.edu", CenaRole.ADMIN, "school-nazareth-01", "ar", "free"),
        ("moderator@cena-demo.edu", CenaRole.MODERATOR, "school-haifa-01", "he", "free"),
    ];

    /// <summary>
    /// Syncs Firebase custom claims for admin-level demo users.
    /// Looks up each user by email and sets role/school_id/locale/plan claims.
    /// Idempotent — safe to run on every startup. Skips users not found in Firebase.
    /// </summary>
    public static async Task SyncAdminClaimsAsync(ILogger logger)
    {
        if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
        {
            logger.LogWarning("Firebase Admin SDK not initialized — skipping claims sync");
            return;
        }

        int synced = 0;
        foreach (var (email, role, school, locale, plan) in AdminUsers)
        {
            try
            {
                var user = await FirebaseAuth.DefaultInstance.GetUserByEmailAsync(email);

                var claims = new Dictionary<string, object>
                {
                    ["role"] = role.ToString(),
                    ["locale"] = locale,
                    ["plan"] = plan,
                };

                if (school is not null)
                    claims["school_id"] = school;

                // Only update if claims differ (avoid unnecessary Firebase writes)
                if (!ClaimsMatch(user.CustomClaims, claims))
                {
                    await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(user.Uid, claims);
                    logger.LogInformation(
                        "Synced Firebase claims for {Email} ({Role})", email, role);
                    synced++;
                }
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
            {
                logger.LogDebug("Firebase user not found: {Email} — skipping claims sync", email);
            }
            catch (Exception ex)
            {
                logger.LogDebug("Skipping Firebase claims for {Email}: {Error}", email, ex.Message);
            }
        }

        if (synced > 0)
            logger.LogInformation("Firebase claims sync: {Synced} admin users updated", synced);
        else
            logger.LogInformation("Firebase claims sync: all admin users already up to date");
    }

    private static bool ClaimsMatch(
        IReadOnlyDictionary<string, object>? existing,
        Dictionary<string, object> expected)
    {
        if (existing is null || existing.Count != expected.Count)
            return false;

        foreach (var (key, value) in expected)
        {
            if (!existing.TryGetValue(key, out var existingValue))
                return false;
            if (!string.Equals(existingValue?.ToString(), value.ToString(), StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
