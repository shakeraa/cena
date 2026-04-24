// =============================================================================
// Cena Platform — ExamTarget erasure cascade coverage ratchet (prr-223)
//
// Companion to ErasureCascadeCoversAllPerStudentDocsTest. That test scans
// `*Document.cs` files; this test targets the prr-218 / prr-222 / prr-229
// projections that are NOT Documents but are per-student state that must
// be covered by the prr-152 cascade mechanism:
//
//   1. SkillKeyedMasteryRow (prr-222) — per-student, per-target mastery
//      projection. Carries StudentAnonId; MUST be covered by
//      ExamTargetErasureCascade.
//   2. ExamTargetRetentionExtension (prr-229) — per-student retention
//      opt-in; same cascade.
//   3. ExamTargetAdded_V1 / ExamTargetArchived_V1 — append-only events;
//      covered via ADR-0038 crypto-shred, documented in the cascade's
//      manifest `details` string.
//
// Ratchet rule:
//   Every type in the target projection list MUST be referenced either
//   - in <c>ExamTargetErasureCascade.EraseForStudentAsync</c> source
//     (so it gets row-deleted), OR
//   - in this test's ComplianceAllowlist with a compliance reason.
//
// This is an EXTENSION of the existing arch test, NOT a blanket
// allowlist. Adding a projection here without wiring the cascade or
// documenting the reason fails the test loudly.
// =============================================================================

using System.Text.RegularExpressions;
using Xunit;

namespace Cena.Actors.Tests.Architecture;

public sealed class ErasureCascadeCoversExamTargetProjectionsTest
{
    // Required per-student projection types. Each entry is the class /
    // record name + the relative source path (for the error message).
    // The ratchet fails if any of these is not referenced in the cascade
    // source OR does not have a matching allowlist entry below.
    private static readonly (string TypeName, string RelPath)[] RequiredProjections =
    {
        ("SkillKeyedMasteryRow",
            "src/actors/Cena.Actors/Mastery/SkillKeyedMasteryRow.cs"),
        ("ExamTargetRetentionExtension",
            "src/actors/Cena.Actors/Retention/IExamTargetRetentionExtensionStore.cs"),
    };

    // Event types that are append-only and therefore covered via
    // ADR-0038 crypto-shred rather than row-delete. The cascade's
    // `details` string MUST mention ADR-0038 so the audit trail proves
    // the coverage is intentional — this test enforces that substring.
    private static readonly string[] CryptoShredCoveredEvents =
    {
        "ExamTargetAdded_V1",
        "ExamTargetArchived_V1",
    };

    // Explicit compliance allowlist — mirrors the pattern in
    // ErasureCascadeCoversAllPerStudentDocsTest. Empty today; any future
    // entry MUST cite the ADR / task that authorises the exemption.
    // ReSharper disable once CollectionNeverUpdated.Local
    private static readonly Dictionary<string, string> ComplianceAllowlist
        = new(StringComparer.Ordinal);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root not found.");
    }

    [Fact]
    public void ExamTarget_erasure_cascade_covers_every_required_projection()
    {
        var repoRoot = FindRepoRoot();
        var cascadePath = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Rtbf", "ExamTargetErasureCascade.cs");

        Assert.True(File.Exists(cascadePath),
            $"Expected cascade at {cascadePath}. If the file moved, update " +
            "this arch test's cascadePath constant.");

        var cascadeSrc = File.ReadAllText(cascadePath);
        var violations = new List<string>();

        foreach (var (typeName, relPath) in RequiredProjections)
        {
            // The projection's type name must appear in the cascade source
            // (either directly or through a method call on its type).
            // This is a coarse check — a reviewer should still verify
            // semantic correctness in the cascade PR. The point of the
            // ratchet is to catch SILENT drift, not to replace review.
            var covered = cascadeSrc.Contains(typeName, StringComparison.Ordinal);
            if (!covered && !ComplianceAllowlist.ContainsKey(typeName))
            {
                violations.Add(
                    $"{relPath} — `{typeName}` carries StudentAnonId but is " +
                    "NOT referenced in ExamTargetErasureCascade.cs. Either " +
                    "thread the projection through the cascade, or add an " +
                    "entry to ComplianceAllowlist in this test with an " +
                    "ADR/task reason.");
            }
        }

        // Verify crypto-shred coverage for append-only events is documented.
        foreach (var eventType in CryptoShredCoveredEvents)
        {
            // Event source file exists
            var eventRelPath = Path.Combine(
                "src", "actors", "Cena.Actors", "ExamTargets", "ExamTargetEvents.cs");
            var eventFull = Path.Combine(repoRoot, eventRelPath);
            Assert.True(File.Exists(eventFull),
                $"Expected event source at {eventRelPath}.");
            var eventSrc = File.ReadAllText(eventFull);
            Assert.True(
                eventSrc.Contains(eventType, StringComparison.Ordinal),
                $"Expected event record `{eventType}` in {eventRelPath}.");
        }

        // Cascade details MUST cite ADR-0038 so the manifest audit line
        // documents the crypto-shred coverage for append-only streams.
        Assert.Matches(
            new Regex(@"ADR-0038", RegexOptions.Compiled),
            cascadeSrc);

        // Cascade MUST be registered via DI — otherwise RightToErasureService
        // never invokes it. The registration lives in
        // ExamTargetRetentionServiceRegistration.cs under the Retention folder.
        var registrationPath = Path.Combine(repoRoot,
            "src", "actors", "Cena.Actors", "Retention",
            "ExamTargetRetentionServiceRegistration.cs");
        Assert.True(File.Exists(registrationPath),
            "Expected DI registration at " + registrationPath);
        var regSrc = File.ReadAllText(registrationPath);
        Assert.Contains("ExamTargetErasureCascade", regSrc, StringComparison.Ordinal);
        Assert.Contains("IErasureProjectionCascade", regSrc, StringComparison.Ordinal);

        if (violations.Count > 0)
        {
            Assert.Fail(
                "prr-223 violation:\n  " + string.Join("\n  ", violations));
        }
    }
}
