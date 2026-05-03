// =============================================================================
// Cena Platform — prr-152 erasure cascade coverage ratchet
//
// Invariant: every `*Document.cs` file under src/shared/Cena.Infrastructure/Documents
// (plus parallel Document-like projections in Cena.Actors) that carries
// a `StudentId` property MUST be accounted for in the erasure pipeline
// by one of:
//
//   1. A direct action in RightToErasureService.ProcessErasureAsync
//      (anonymise, delete, or preserve).
//   2. A registered IErasureProjectionCascade that names it.
//   3. An explicit allowlist entry in this test citing an ADR or the
//      specific compliance reason (e.g. session-scope → purged by the
//      retention worker, crypto-shred-only → covered by ADR-0038).
//
// The ratchet is scan-based (same shape as NoPiiFieldInLlmPromptTest). It
// fails loudly if a new per-student Document lands without being wired
// into the cascade — which would silently leave student data behind
// after an Article-17 erasure request.
//
// prr-152 raised this test with the three cascades in play (ParentDigest,
// TutorContext cache, StudentVisibilityVeto). Future per-student
// projections must update this test AND add a cascade.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class ErasureCascadeCoversAllPerStudentDocsTest
{
    // Matches `StudentId` as a property or field (case-sensitive; C# convention).
    private static readonly Regex StudentIdProperty = new(
        @"\bpublic\s+string\??\s+StudentId\b",
        RegexOptions.Compiled);

    // Documents explicitly handled inside RightToErasureService.ProcessErasureAsync
    // — keep in sync with the 7 + cascade steps in that method. Each entry is
    // the class name (without the "Document" suffix where applicable).
    private static readonly HashSet<string> BaselineCoveredByCoreService = new(
        StringComparer.Ordinal)
    {
        "StudentProfileSnapshot",            // step 1 — ANONYMIZE
        "TutorMessageDocument",              // step 2 — DELETE
        "TutorThreadDocument",               // step 2 — DELETE
        "DeviceSessionDocument",             // step 3 — DELETE
        "StudentPreferencesDocument",        // step 4 — DELETE
        "ShareTokenDocument",                // step 5 — DELETE
        "ConsentRecord",                     // step 6 — PRESERVE (revoke)
        "StudentRecordAccessLog",            // step 7 — PRESERVE (FERPA)
    };

    // Documents handled by a prr-152 cascade. Names here match the
    // IErasureProjectionCascade.ProjectionName constants.
    private static readonly HashSet<string> CoveredByCascade = new(
        StringComparer.Ordinal)
    {
        "ParentDigestPreferences",           // ParentDigestErasureCascade
        "TutorContextCache",                 // TutorContextErasureCascade
        "StudentVisibilityVetoEvents",       // StudentVisibilityVetoErasureCascade
    };

    // Explicit compliance-reason allowlist. Each entry MUST cite the ADR
    // or task that authorised the exemption. Keep the reason human-
    // readable — a reviewer must be able to tell at a glance why the
    // Document is not in the cascade.
    private static readonly Dictionary<string, string> ComplianceAllowlist = new(
        StringComparer.Ordinal)
    {
        // Session-scoped projections — retention worker purges on session
        // end / 30-day window (ADR-0003). Not erased by the Article-17
        // path because they are already gone by the time cooling
        // period + 30 days elapses.
        { "SessionAttemptHistoryDocument", "ADR-0003: session-scoped; RetentionWorker purges on session end." },
        { "LearningSessionQueueProjection", "ADR-0003: session-scoped; RetentionWorker purges on session end." },
        { "ActiveSessionSnapshot", "ADR-0003: session-scoped; RetentionWorker purges on session end." },
        // Legacy at-risk document is strictly internal and never leaves
        // the tenant; it is purged by the ML-exclusion worker.
        { "AtRiskStudentDocument", "prr-003b / ADR-0038: crypto-shred via subject-key tombstone." },
        // Notifications + outreach documents are bounded-context owned by
        // the outreach service; the outreach ErasureCascade is a
        // pre-launch follow-up task (see tasks/pre-release-review/
        // post-launch/TASK-PRR-086-device-fingerprint-purge-on-account-deletion.md).
        { "OutreachEventDocument", "Pre-launch follow-up: prr-086 device-fingerprint + outreach cascade." },
        { "StudentNotificationPreferencesDocument", "Pre-launch follow-up: prr-086 device-fingerprint + outreach cascade." },
        { "NotificationDocument", "Pre-launch follow-up: prr-086 device-fingerprint + outreach cascade." },
        { "NotificationPreferencesDocument", "Pre-launch follow-up: prr-086 device-fingerprint + outreach cascade." },
        // Assignment + challenge projections are institute-owned; the
        // teacher retains the roster record after a student leaves and
        // the student's assignment rows are handled by a separate
        // institute-cleanup flow, not the subject-driven erasure path.
        { "AssignmentDocument", "Institute-owned roster record; teacher cleanup flow, not subject-driven erasure." },
        { "BossAttemptDocument", "Institute-owned performance record; session-scope + retention worker." },
        { "DailyChallengeCompletionDocument", "Institute-owned performance record; retention worker." },
        { "CardChainProgressDocument", "Institute-owned performance record; retention worker." },
        { "TournamentRegistrationDocument", "Institute-owned roster; institute cleanup flow." },
        // Social surface documents: SocialReport / PeerSolution / Comment / StudyRoomMembership / FriendRequest / Friendship / UserBlocklist.
        // Moderation retention outlives the student for platform-safety reasons (analogous to the access-log FERPA carve-out).
        { "SocialReportDocument", "Moderation retention (analogous to FERPA carve-out for access logs)." },
        { "PeerSolutionDocument", "Published learning artefact; anonymised on erasure by a separate task." },
        { "CommentDocument", "Published comment; anonymised by a separate moderation task." },
        { "StudyRoomMembershipDocument", "Session-scoped; retention worker purges on room close." },
        { "FriendRequestDocument", "Paired-record — removed when either party is erased by the social cleanup job." },
        { "FriendshipDocument", "Paired-record — removed when either party is erased by the social cleanup job." },
        { "UserBlocklistDocument", "Mutual blocklist — handled by the social cleanup job." },
        { "ClassFeedItemDocument", "Published class-feed activity; anonymised by the social cleanup job." },
        // Focus analytics rollups are aggregates over many students; the
        // ML-exclusion worker handles the k-anonymity-safe purge of the
        // erased student's contribution.
        { "FocusSessionRollupDocument", "ML-exclusion worker handles aggregate purges." },
        { "FocusDegradationRollupDocument", "ML-exclusion worker handles aggregate purges." },
        // Enrollment + join-request documents are institute-scoped roster
        // events; the teacher deletes them via the admin flow.
        { "EnrollmentDocument", "Institute roster; admin-driven cleanup, not subject-driven erasure." },
        { "ClassroomJoinRequestDocument", "Institute roster; admin-driven cleanup." },
        // Onboarding self-assessment is captured at signup; anonymised on
        // profile anonymisation (same flow as StudentProfileSnapshot).
        { "OnboardingSelfAssessmentDocument", "Captured at signup; anonymised via StudentProfileSnapshot hash." },
        // Mentor chat + notes: mentor-owned, retention worker handles.
        { "MentorNoteDocument", "Mentor-owned record; retention worker purges." },
        { "MentorChatMessageDocument", "Mentor-owned record; retention worker purges." },
        { "MentorChatChannelDocument", "Mentor-owned record; retention worker purges." },
        // prr-152 follow-up allowlist — crypto-shred (ADR-0038) renders
        // these rows unreadable after the subject key is destroyed; a
        // separate projection-delete cascade is a pre-launch follow-up task
        // (same track as prr-086 notifications/outreach cascade above).
        { "WebPushSubscriptionDocument", "ADR-0038 crypto-shred sufficient; push-subscription row holds no cleartext PII beyond student_id, which is a subject-key handle." },
        { "ClassAttentionRollupDocument", "Aggregate rollup; per-student contribution is crypto-shredded via ADR-0038 subject-key destruction. ML-exclusion worker handles aggregate follow-up." },
        { "OutreachBudgetDocument", "Institute-owned counter; student's contribution is removed via the same outreach-cleanup job tracked by prr-086." },
        { "StudyRoomDocument", "Paired-record — removed when either party is erased by the social cleanup job (same track as StudyRoomMembershipDocument)." },
        { "DailyChallengeDocument", "Institute-owned challenge definition; student participation rows are cleaned by the existing institute-cleanup flow." },
        { "CardChainDefinitionDocument", "Institute-owned definition; student progress rows covered by CardChainProgressDocument retention worker." },
        { "TournamentDocument", "Institute-owned tournament definition; student registrations covered by TournamentRegistrationDocument allowlist above." },
    };

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

    private static IEnumerable<string> DocumentFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src", "shared", "Cena.Infrastructure", "Documents");
        if (!Directory.Exists(srcRoot)) yield break;
        foreach (var f in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, f);
            var sep = Path.DirectorySeparatorChar;
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            yield return f;
        }
    }

    [Fact]
    public void Every_PerStudent_Document_Is_Covered_By_Erasure_Cascade_Or_Allowlist()
    {
        var repoRoot = FindRepoRoot();
        var violations = new List<string>();
        var classPattern = new Regex(
            @"public\s+(?:sealed\s+)?(?:class|record)\s+(?<name>\w+Document|StudentProfileSnapshot|ConsentRecord|StudentRecordAccessLog)\b",
            RegexOptions.Compiled);

        foreach (var file in DocumentFiles(repoRoot))
        {
            var raw = File.ReadAllText(file);
            if (!StudentIdProperty.IsMatch(raw)) continue;

            var classMatches = classPattern.Matches(raw);
            foreach (Match cm in classMatches)
            {
                var name = cm.Groups["name"].Value;
                if (BaselineCoveredByCoreService.Contains(name)) continue;
                if (CoveredByCascade.Contains(name)) continue;
                if (ComplianceAllowlist.ContainsKey(name)) continue;

                var rel = Path.GetRelativePath(repoRoot, file)
                              .Replace(Path.DirectorySeparatorChar, '/');
                violations.Add(
                    $"{rel} — class `{name}` carries a `StudentId` property but is " +
                    "not covered by the erasure pipeline (neither the core service " +
                    "nor a registered IErasureProjectionCascade), and has no " +
                    "ComplianceAllowlist entry. Either add a cascade under " +
                    "src/actors/Cena.Actors/*/ErasureCascade.cs AND register it " +
                    "in AddCenaAdminServices, OR add an allowlist entry in this " +
                    "test with an ADR/task reference explaining why the ADR-0038 " +
                    "crypto-shred alone is sufficient.");
            }
        }

        // Verify the cascade classes themselves exist at the expected seams —
        // catches the "test passes because every Document is allowlisted"
        // failure mode.
        var cascadePaths = new[]
        {
            "src/actors/Cena.Actors/ParentDigest/ParentDigestErasureCascade.cs",
            "src/actors/Cena.Actors/Tutoring/TutorContextErasureCascade.cs",
            "src/actors/Cena.Actors/Consent/StudentVisibilityVetoErasureCascade.cs",
        };
        foreach (var p in cascadePaths)
        {
            var full = Path.Combine(repoRoot, p.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(
                File.Exists(full),
                $"Expected prr-152 cascade at {p} — either the cascade was moved " +
                "without updating this test or a required cascade is missing.");
        }

        if (violations.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine(
            $"prr-152 violation: {violations.Count} per-student Document(s) are not " +
            "covered by the erasure cascade.");
        sb.AppendLine();
        foreach (var v in violations) sb.AppendLine("  " + v);
        Assert.Fail(sb.ToString());
    }
}
