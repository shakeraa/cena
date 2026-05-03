// =============================================================================
// Cena Platform — TeacherOverrideNoCrossTenantTest (prr-150)
//
// ADR-0001 regression gate for the prr-150 bounded context. Every PUBLIC
// command method in TeacherOverrideCommands.cs MUST call VerifyTenantScope
// before appending an event. A teacher at institute A writing to a student
// enrolled at institute B would be a tenant-isolation violation of the
// kind ADR-0001 explicitly forbids.
//
// Strategy:
//   - Find the repo root (CLAUDE.md anchor).
//   - Read src/actors/Cena.Actors/Teacher/ScheduleOverride/TeacherOverrideCommands.cs.
//   - For each `public async Task *Async(` declaration whose signature
//     includes a command DTO (PinTopicCommand / AdjustBudgetCommand /
//     OverrideMotivationCommand), walk forward to the closing brace and
//     fail unless the method body contains `VerifyTenantScope(` before
//     any `_store.AppendAsync(` call.
//   - Also assert VerifyTenantScope itself throws CrossTenantOverrideDenied
//     when the institute mismatches.
//
// Static scan is intentional — adding a new command that forgets the
// guard must fail CI without anyone having to remember to update a
// runtime checklist.
// =============================================================================

using System.Text;
using System.Text.RegularExpressions;
using Cena.Actors.Mastery;
using Cena.Actors.Teacher.ScheduleOverride;

namespace Cena.Actors.Tests.Architecture;

public class TeacherOverrideNoCrossTenantTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string CommandsFilePath(string repoRoot) => Path.Combine(
        repoRoot, "src", "actors", "Cena.Actors", "Teacher", "ScheduleOverride",
        "TeacherOverrideCommands.cs");

    // Matches a public async Task method on a *Command parameter. We deliberately
    // scope to the three expected command shapes; adding a new command type
    // means also updating this regex, which is the right friction level for a
    // tenant-isolation gate.
    private static readonly Regex CommandMethodSignature = new(
        @"public\s+async\s+Task\s+(?<method>[A-Z][A-Za-z0-9]*Async)\s*\(\s*(?<dto>PinTopicCommand|AdjustBudgetCommand|OverrideMotivationCommand)\b",
        RegexOptions.Compiled);

    [Fact]
    public void Every_command_method_invokes_VerifyTenantScope_before_AppendAsync()
    {
        var repoRoot = FindRepoRoot();
        var path = CommandsFilePath(repoRoot);
        Assert.True(File.Exists(path), $"TeacherOverrideCommands.cs missing at {path}");
        var source = File.ReadAllText(path);

        var matches = CommandMethodSignature.Matches(source);
        Assert.NotEmpty(matches); // If this fails, the regex drifted.

        var failures = new StringBuilder();
        foreach (Match m in matches)
        {
            var (body, method) = ExtractMethodBody(source, m.Index, m.Groups["method"].Value);

            var verifyIdx = body.IndexOf("VerifyTenantScope(", StringComparison.Ordinal);
            var appendIdx = body.IndexOf("_store.AppendAsync(", StringComparison.Ordinal);

            if (verifyIdx < 0)
            {
                failures.AppendLine(
                    $"  {method}: no VerifyTenantScope(...) call found. Every teacher-override " +
                    $"command MUST enforce the ADR-0001 tenant invariant before touching the store.");
                continue;
            }
            if (appendIdx < 0)
            {
                failures.AppendLine(
                    $"  {method}: no _store.AppendAsync(...) call found. Expected the command to " +
                    $"persist through the aggregate store.");
                continue;
            }
            if (verifyIdx > appendIdx)
            {
                failures.AppendLine(
                    $"  {method}: VerifyTenantScope(...) appears AFTER _store.AppendAsync(...). " +
                    $"The guard MUST run before the event is appended, otherwise a cross-tenant " +
                    $"write lands in the stream even though the call throws on return.");
            }
        }

        Assert.True(failures.Length == 0,
            "TeacherOverride tenant-isolation gate failed (ADR-0001):\n" + failures);
    }

    [Fact]
    public async Task VerifyTenantScope_throws_on_institute_mismatch()
    {
        var store = new InMemoryTeacherOverrideStore();
        var lookup = new InMemoryStudentInstituteLookup();
        lookup.Set("stu", "inst-b");
        var cmds = new TeacherOverrideCommands(store, lookup);

        await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.PinTopicAsync(new PinTopicCommand(
                StudentAnonId: "stu",
                TopicSlug: "t",
                PinnedSessionCount: 1,
                TeacherActorId: "teacher",
                TeacherInstituteId: "inst-a",
                Rationale: "r",
                SetAt: DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task VerifyTenantScope_denies_missing_enrollment_to_prevent_existence_leak()
    {
        var store = new InMemoryTeacherOverrideStore();
        var lookup = new InMemoryStudentInstituteLookup(); // no enrollment
        var cmds = new TeacherOverrideCommands(store, lookup);

        await Assert.ThrowsAsync<CrossTenantOverrideDeniedException>(() =>
            cmds.OverrideMotivationAsync(new OverrideMotivationCommand(
                StudentAnonId: "stu",
                SessionTypeScope: "all",
                OverrideProfile: MotivationProfile.Confident,
                TeacherActorId: "teacher",
                TeacherInstituteId: "inst-a",
                Rationale: "r",
                SetAt: DateTimeOffset.UtcNow)));
    }

    /// <summary>
    /// Extracts the body substring from the opening `{` following the
    /// method signature through the matching closing `}`. Brace-depth
    /// counting; treats braces inside `"..."` and `'...'` as literal.
    /// </summary>
    private static (string Body, string Method) ExtractMethodBody(
        string source, int sigIndex, string method)
    {
        var openIdx = source.IndexOf('{', sigIndex);
        if (openIdx < 0) return (string.Empty, method);
        var depth = 0;
        for (var i = openIdx; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"' || c == '\'')
            {
                // Skip through closing quote, respecting backslash escape.
                var quote = c;
                i++;
                while (i < source.Length && source[i] != quote)
                {
                    if (source[i] == '\\') i++;
                    i++;
                }
                continue;
            }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return (source.Substring(openIdx, i - openIdx + 1), method);
            }
        }
        return (source[openIdx..], method);
    }
}
