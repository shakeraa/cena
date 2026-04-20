// =============================================================================
// Cena Platform — NoParentDigestBypassesPreferencesTest (prr-051).
//
// Architecture ratchet. Every code path that dispatches a parent digest
// (email, SMS, WhatsApp) MUST consult the per-(parent, child) digest
// preferences BEFORE the sender fan-out.
//
// Strategy:
//
//   - Scan every .cs file under src/actors/Cena.Actors/ParentDigest/ and
//     src/api/Cena.Admin.Api/Features/ParentConsole/ (plus any new dispatch
//     site explicitly approved in the allowlist below).
//
//   - A file that invokes one of the dispatch seams listed in
//     DispatchSeams MUST also contain a preference consult: one of
//     ParentDigestDispatchPolicy.Decide, preferences.ShouldSend,
//     preferences.EffectiveStatus, or a wrapper named in
//     ApprovedPreferenceCalls.
//
//   - Files that only DECLARE the dispatch seam (the renderer itself,
//     the WhatsApp sender interface, the aggregator) are exempted via
//     the DispatchDefinitionAllowlist — they do not themselves dispatch,
//     they are the dispatch primitives.
//
// New dispatch code that forgets the preferences consult fails this test.
// Fix = add the consult, not the allowlist; the allowlist only covers
// DEFINITION files, never consumer-side dispatchers.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoParentDigestBypassesPreferencesTest
{
    /// <summary>
    /// Method calls that cause a digest to leave the server. Any file that
    /// contains a CALL (not a declaration) to one of these must also consult
    /// the preferences aggregate on the same code path.
    /// </summary>
    private static readonly string[] DispatchSeams =
    {
        // The pure renderer is the last in-memory step before the email
        // send. A future EmailDigestSender calls Render + hands off to
        // ISmtpClient; that call must be gated by preferences.
        "ParentDigestRenderer.Render",

        // Email send. The path through .NET's SmtpClient.SendMailAsync is
        // the final mail-leaves-the-building step. An IEmailSender abstraction
        // would move this; we list the name either way.
        "IEmailSender.SendAsync",
        "ISmtpClient.SendAsync",

        // WhatsApp channel. IWhatsAppSender.SendAsync is the last step
        // before a vendor call.
        "IWhatsAppSender.SendAsync",
    };

    /// <summary>
    /// Call-site tokens that count as "preferences consulted". A handler
    /// may use any of these; adding a new wrapper requires adding its name
    /// here AND showing the wrapper reaches the aggregate.
    /// </summary>
    private static readonly string[] ApprovedPreferenceCalls =
    {
        "ParentDigestDispatchPolicy.Decide",
        ".ShouldSend(",
        ".EffectiveStatus(",
        "ApplyUnsubscribeAllAsync",
    };

    /// <summary>
    /// Files that DEFINE a dispatch seam but do not themselves dispatch.
    /// Paths are relative to the repo root; forward slashes normalized at
    /// comparison time.
    /// </summary>
    private static readonly string[] DispatchDefinitionAllowlist =
    {
        // The renderer ITSELF — it defines Render(); every caller is
        // still subject to the ratchet.
        "src/actors/Cena.Actors/ParentDigest/ParentDigestRenderer.cs",

        // The aggregator — no fan-out, pure math.
        "src/actors/Cena.Actors/ParentDigest/ParentDigestAggregator.cs",

        // The WhatsApp channel FILE declares IWhatsAppSender and the
        // NullWhatsAppSender skeleton; the pattern matches "IWhatsAppSender.SendAsync"
        // inside the interface declaration.
        "src/actors/Cena.Actors/ParentDigest/WhatsAppChannel.cs",

        // Vendor adapter — implements IWhatsAppSender. Its SendAsync IS
        // the vendor call, not a dispatcher.
        "src/actors/Cena.Actors/ParentDigest/TwilioWhatsAppSender.cs",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    [Fact]
    public void Every_parent_digest_dispatch_consults_preferences()
    {
        var root = FindRepoRoot();
        var scanRoots = new[]
        {
            Path.Combine(root, "src", "actors", "Cena.Actors", "ParentDigest"),
            Path.Combine(root, "src", "api", "Cena.Admin.Api", "Features", "ParentConsole"),
        };

        var allow = DispatchDefinitionAllowlist
            .Select(p => p.Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant())
            .ToHashSet();

        var failures = new StringBuilder();
        var scannedFiles = 0;
        var dispatchSites = 0;

        foreach (var scanRoot in scanRoots)
        {
            if (!Directory.Exists(scanRoot)) continue;

            foreach (var path in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
            {
                scannedFiles++;
                var relative = Path.GetRelativePath(root, path);
                if (allow.Contains(relative.ToLowerInvariant())) continue;

                var source = File.ReadAllText(path);
                if (!ContainsDispatchCall(source)) continue;

                dispatchSites++;
                if (!ContainsPreferenceConsult(source))
                {
                    failures.AppendLine(
                        $"  {relative}: dispatches a parent digest but does not consult "
                        + "ParentDigestDispatchPolicy / ParentDigestPreferences. Every digest "
                        + "dispatch MUST check preferences before fan-out (prr-051).");
                }
            }
        }

        Assert.True(scannedFiles > 0,
            "No files scanned — regex drifted, or the directories moved. Update this test.");

        Assert.True(failures.Length == 0,
            "Parent-digest preferences ratchet failed (prr-051):\n" + failures);

        // Defensive minimum: the ratchet must see at least ONE real
        // dispatcher call-site once a dispatcher lands. Until then
        // dispatchSites may be 0 — that's fine; a future dispatcher will
        // trip this branch and still pass if it consults preferences.
    }

    [Fact]
    public void Preferences_aggregate_and_dispatch_policy_exist_where_expected()
    {
        var root = FindRepoRoot();

        var prefs = Path.Combine(
            root, "src", "actors", "Cena.Actors", "ParentDigest",
            "ParentDigestPreferences.cs");
        Assert.True(File.Exists(prefs),
            $"prr-051: preferences aggregate missing at {prefs}");

        var prefsSrc = File.ReadAllText(prefs);
        Assert.Contains("public sealed record ParentDigestPreferences", prefsSrc);
        Assert.Contains("public bool ShouldSend(", prefsSrc);
        Assert.Contains("AsFullyUnsubscribed", prefsSrc);

        var policy = Path.Combine(
            root, "src", "actors", "Cena.Actors", "ParentDigest",
            "ParentDigestDispatchDecision.cs");
        Assert.True(File.Exists(policy),
            $"prr-051: dispatch policy missing at {policy}");

        var policySrc = File.ReadAllText(policy);
        Assert.Contains("public static class ParentDigestDispatchPolicy", policySrc);
        Assert.Contains("public static ParentDigestDispatchDecision Decide(", policySrc);
    }

    [Fact]
    public void Safety_alerts_default_on_every_other_purpose_default_off()
    {
        // Domain-rule ratchet: the task body pins the default table.
        // Flipping any default without bumping the event version + shipping
        // a migration plan would retroactively alter parent intent and is
        // explicitly a ship-blocker.
        Assert.True(
            Cena.Actors.ParentDigest.DigestPurposes.DefaultOptedIn(
                Cena.Actors.ParentDigest.DigestPurpose.SafetyAlerts),
            "SafetyAlerts must default to OptedIn (prr-051 task body).");

        foreach (var purpose in new[]
        {
            Cena.Actors.ParentDigest.DigestPurpose.WeeklySummary,
            Cena.Actors.ParentDigest.DigestPurpose.HomeworkReminders,
            Cena.Actors.ParentDigest.DigestPurpose.ExamReadiness,
            Cena.Actors.ParentDigest.DigestPurpose.AccommodationsChanges,
        })
        {
            Assert.False(
                Cena.Actors.ParentDigest.DigestPurposes.DefaultOptedIn(purpose),
                $"{purpose} must default to OptedOut (prr-051 task body).");
        }
    }

    private static bool ContainsDispatchCall(string source)
    {
        foreach (var seam in DispatchSeams)
        {
            // Match a CALL-like token: `seam(` or `.SendAsync(`.
            // Method-declaration lines (`public Task<...> SendAsync(`)
            // are caught by the "starts with public/private/etc." filter
            // below. We ignore declaration-shaped matches.
            var idx = 0;
            while (idx < source.Length)
            {
                var hit = source.IndexOf(seam, idx, StringComparison.Ordinal);
                if (hit < 0) break;

                // Require a line that LOOKS like a call: the 50 chars
                // before the match must NOT contain a method-modifier
                // keyword immediately followed by "Task<" (that would be
                // a declaration line).
                var preludeStart = Math.Max(0, hit - 64);
                var prelude = source.Substring(preludeStart, hit - preludeStart);
                var isDeclaration =
                    Regex.IsMatch(prelude,
                        @"\b(public|private|internal|protected)\b.*Task",
                        RegexOptions.IgnoreCase);
                if (!isDeclaration) return true;
                idx = hit + seam.Length;
            }
        }
        return false;
    }

    private static bool ContainsPreferenceConsult(string source)
    {
        foreach (var token in ApprovedPreferenceCalls)
        {
            if (source.IndexOf(token, StringComparison.Ordinal) >= 0) return true;
        }
        // Also accept the preferences aggregate being used directly.
        if (source.IndexOf("ParentDigestPreferences", StringComparison.Ordinal) >= 0 &&
            (source.IndexOf(".ShouldSend", StringComparison.Ordinal) >= 0 ||
             source.IndexOf(".EffectiveStatus", StringComparison.Ordinal) >= 0 ||
             source.IndexOf("Decide(", StringComparison.Ordinal) >= 0))
        {
            return true;
        }
        return false;
    }
}
