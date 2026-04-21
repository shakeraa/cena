// =============================================================================
// Cena Platform — BagrutFamilyRequiresQuestionPapersTest (prr-243, ADR-0050 §1)
//
// Architecture regression guards for the PRR-243 invariant:
// "Any ExamTarget with Bagrut-family examCode has ≥1 paper code in its
// event stream."
//
// Enforcement vectors:
//
//   1. The ExamTarget record carries QuestionPaperCodes as non-nullable
//      IReadOnlyList<string>. A future edit that removes the field, makes
//      it nullable, or renames it breaks downstream replay; this test
//      catches it at test-time before the compile error cascades.
//
//   2. StudentPlanCommandHandler emits CommandError.QuestionPaperCodesRequired
//      when a Bagrut AddExamTargetCommand carries no papers, and enforces
//      CommandError.QuestionPaperRemovalLeavesEmpty on Remove. Both
//      literal string references must remain reachable from the handler.
//
//   3. The ExamCodeFamilyClassifier's BAGRUT_* prefix rule — deleting the
//      rule weakens the Bagrut invariant to Other family (papers optional)
//      and is a silent regression.
//
// These are textual gates — they scan the shipped source for the exact
// invariant-bearing symbols. Mirrors the SocraticCapEnforcedTest /
// DailyMinuteCapArchitectureTest pattern.
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Architecture;

public sealed class BagrutFamilyRequiresQuestionPapersTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root (CLAUDE.md) not found");
    }

    private static string ReadSource(params string[] relParts)
    {
        var repoRoot = FindRepoRoot();
        var file = Path.Combine(new[] { repoRoot }.Concat(relParts).ToArray());
        Assert.True(File.Exists(file), $"Expected source file at {file}");
        return File.ReadAllText(file);
    }

    // ── 1. ExamTarget carries the field ─────────────────────────────────

    [Fact]
    public void ExamTarget_record_declares_QuestionPaperCodes_field()
    {
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "ExamTarget.cs");
        // Record parameter list is reflowed across lines — match the type + name
        // literally. The type is non-nullable IReadOnlyList<string>.
        Assert.Matches(
            new Regex(@"IReadOnlyList<string>\s+QuestionPaperCodes\b",
                RegexOptions.Singleline),
            src);
    }

    [Fact]
    public void ExamTarget_record_declares_PerPaperSittingOverride_field()
    {
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "ExamTarget.cs");
        Assert.Matches(
            new Regex(@"IReadOnlyDictionary<string,\s*SittingCode>\?\s+PerPaperSittingOverride\b",
                RegexOptions.Singleline),
            src);
    }

    // ── 2. Command handler enforces the invariants ──────────────────────

    [Fact]
    public void CommandHandler_emits_QuestionPaperCodesRequired_error()
    {
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "StudentPlanCommandHandler.QuestionPapers.cs");
        Assert.Contains("CommandError.QuestionPaperCodesRequired", src);
    }

    [Fact]
    public void CommandHandler_emits_QuestionPaperRemovalLeavesEmpty_error()
    {
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "StudentPlanCommandHandler.QuestionPapers.cs");
        Assert.Contains("CommandError.QuestionPaperRemovalLeavesEmpty", src);
    }

    [Fact]
    public void CommandHandler_QuestionPapers_partial_has_Bagrut_family_gate()
    {
        // The Bagrut-family check on Remove must specifically test
        // QuestionPaperCodes.Count to guard the empty-target condition.
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "StudentPlanCommandHandler.QuestionPapers.cs");
        Assert.Matches(
            new Regex(@"target\.Family\s*==\s*ExamCodeFamily\.Bagrut",
                RegexOptions.Singleline),
            src);
    }

    // ── 3. Classifier's BAGRUT_ prefix rule is preserved ───────────────

    [Fact]
    public void Classifier_retains_BAGRUT_underscore_prefix_rule()
    {
        var src = ReadSource("src", "actors", "Cena.Actors", "StudentPlan", "ExamTarget.cs");
        Assert.Matches(
            new Regex(@"StartsWith\(""BAGRUT_""",
                RegexOptions.Singleline),
            src);
    }

    [Theory]
    [InlineData("BAGRUT_MATH_5U")]
    [InlineData("BAGRUT_MATH_4U")]
    [InlineData("BAGRUT_MATH_3U")]
    [InlineData("BAGRUT_ENGLISH_MODULE_A")]
    [InlineData("BAGRUT_MATH_5U_ARAB_STREAM")]
    public void Classifier_Bagrut_codes_classify_as_Bagrut(string code)
    {
        Assert.Equal(ExamCodeFamily.Bagrut, ExamCodeFamilyClassifier.Classify(new ExamCode(code)));
    }

    // ── 4. End-to-end invariant: a Bagrut stream with no papers rejects ─

    [Fact]
    public async Task Bagrut_target_with_zero_papers_cannot_be_added_end_to_end()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store);

        var cmd = new AddExamTargetCommand(
            StudentAnonId: "stu-arch-1",
            Source: ExamTargetSource.Student,
            AssignedById: new UserId("stu-arch-1"),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: null,
            QuestionPaperCodes: Array.Empty<string>());

        var result = await handler.HandleAsync(cmd);

        Assert.False(result.Success);
        Assert.Equal(CommandError.QuestionPaperCodesRequired, result.Error);
    }

    [Fact]
    public async Task Bagrut_target_with_one_paper_and_remove_leaves_zero_rejects()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var handler = new StudentPlanCommandHandler(store);

        var add = await handler.HandleAsync(new AddExamTargetCommand(
            StudentAnonId: "stu-arch-2",
            Source: ExamTargetSource.Student,
            AssignedById: new UserId("stu-arch-2"),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: new SittingCode("תשפ״ו", SittingSeason.Summer, SittingMoed.A),
            WeeklyHours: 5,
            ReasonTag: null,
            QuestionPaperCodes: new[] { "035581" }));

        Assert.True(add.Success);

        var remove = await handler.HandleAsync(
            new RemoveQuestionPaperCommand("stu-arch-2", add.TargetId!.Value, "035581"));

        Assert.False(remove.Success);
        Assert.Equal(CommandError.QuestionPaperRemovalLeavesEmpty, remove.Error);
    }
}
