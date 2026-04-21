// =============================================================================
// Cena Platform — QuestionPaper command handler tests (prr-243, ADR-0050 §1)
//
// Exhaustive invariant coverage for the four PRR-243 commands:
//   - AddQuestionPaperCommand
//   - RemoveQuestionPaperCommand
//   - SetPerPaperSittingOverrideCommand
//   - ClearPerPaperSittingOverrideCommand
// Plus the on-create path through AddExamTargetCommand:
//   - Bagrut family requires ≥1 paper (QuestionPaperCodesRequired).
//   - Standardized family forbids papers (QuestionPaperCodesForbidden).
//   - Duplicate paper codes rejected (QuestionPaperCodeDuplicate).
//   - Unknown paper code rejected via catalog validator.
//   - Override keys ⊆ paper codes; values ≠ primary sitting.
//
// Catalog validator is swapped for a test-double so catalog-coupling
// doesn't leak into the actors-layer tests.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.Tests.StudentPlan;

public sealed class QuestionPaperCommandHandlerTests
{
    private const string StudentId = "stu-qp-1";
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-04-21T10:00:00Z");
    private static readonly SittingCode Summer = new("תשפ״ו", SittingSeason.Summer, SittingMoed.A);
    private static readonly SittingCode Winter = new("תשפ״ו", SittingSeason.Winter, SittingMoed.A);
    private static readonly SittingCode SummerB = new("תשפ״ו", SittingSeason.Summer, SittingMoed.B);

    /// <summary>
    /// Catalog double: accepts a fixed set of (examCode, track) → paper-code
    /// triples. Defaults to "Math 5U has papers 035581..035583".
    /// </summary>
    private sealed class FakeCatalog : IQuestionPaperCatalogValidator
    {
        public HashSet<(string exam, string? track, string paper)> Allowed { get; } = new();

        public bool IsPaperCodeValid(ExamCode examCode, TrackCode? track, string paperCode)
            => Allowed.Contains((examCode.Value, track?.Value, paperCode));
    }

    private static (StudentPlanCommandHandler handler, InMemoryStudentPlanAggregateStore store, FakeCatalog cat) Build()
    {
        var store = new InMemoryStudentPlanAggregateStore();
        var cat = new FakeCatalog();
        cat.Allowed.Add(("BAGRUT_MATH_5U", "5U", "035581"));
        cat.Allowed.Add(("BAGRUT_MATH_5U", "5U", "035582"));
        cat.Allowed.Add(("BAGRUT_MATH_5U", "5U", "035583"));
        var handler = new StudentPlanCommandHandler(store, () => FixedNow, cat);
        return (handler, store, cat);
    }

    private static AddExamTargetCommand BagrutCmd(
        int weeklyHours = 5,
        IReadOnlyList<string>? papers = null,
        IReadOnlyDictionary<string, SittingCode>? perPaperOverride = null,
        SittingCode? sitting = null)
        => new(
            StudentAnonId: StudentId,
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(StudentId),
            EnrollmentId: null,
            ExamCode: new ExamCode("BAGRUT_MATH_5U"),
            Track: new TrackCode("5U"),
            Sitting: sitting ?? Summer,
            WeeklyHours: weeklyHours,
            ReasonTag: null,
            QuestionPaperCodes: papers ?? new[] { "035581", "035582", "035583" },
            PerPaperSittingOverride: perPaperOverride);

    private static AddExamTargetCommand PetCmd(
        IReadOnlyList<string>? papers = null)
        => new(
            StudentAnonId: StudentId,
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(StudentId),
            EnrollmentId: null,
            ExamCode: new ExamCode("PET"),
            Track: null,
            Sitting: Summer,
            WeeklyHours: 5,
            ReasonTag: null,
            QuestionPaperCodes: papers);

    // ── ExamCodeFamilyClassifier ────────────────────────────────────────

    [Theory]
    [InlineData("BAGRUT_MATH_5U", ExamCodeFamily.Bagrut)]
    [InlineData("BAGRUT_ENGLISH_MODULE_A", ExamCodeFamily.Bagrut)]
    [InlineData("SAT_MATH", ExamCodeFamily.Standardized)]
    [InlineData("PET", ExamCodeFamily.Standardized)]
    [InlineData("PET_RUSSIAN_VERBAL", ExamCodeFamily.Standardized)]
    [InlineData("IB_MATH_HL", ExamCodeFamily.Other)]
    [InlineData("TAWJIHI_SCIENTIFIC", ExamCodeFamily.Other)]
    public void Classifier_maps_by_prefix(string code, ExamCodeFamily expected)
    {
        Assert.Equal(expected, ExamCodeFamilyClassifier.Classify(new ExamCode(code)));
    }

    // ── Add path: question-paper invariants ─────────────────────────────

    [Fact]
    public async Task Add_Bagrut_rejects_empty_papers()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(papers: Array.Empty<string>()));
        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodesRequired, r.Error);
    }

    [Fact]
    public async Task Add_Bagrut_accepts_minimal_single_paper()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(papers: new[] { "035582" }));
        Assert.True(r.Success);
    }

    [Fact]
    public async Task Add_Bagrut_rejects_duplicate_paper_codes()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581", "035581" }));
        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodeDuplicate, r.Error);
    }

    [Fact]
    public async Task Add_Bagrut_rejects_unknown_paper_code_via_catalog()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(papers: new[] { "999999" }));
        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodeUnknown, r.Error);
    }

    [Fact]
    public async Task Add_Standardized_rejects_any_papers()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(PetCmd(papers: new[] { "anything" }));
        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodesForbidden, r.Error);
    }

    [Fact]
    public async Task Add_Standardized_accepts_empty_papers()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(PetCmd(papers: null));
        Assert.True(r.Success);
    }

    [Fact]
    public async Task Add_perPaperOverride_key_must_be_in_papers()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(
            papers: new[] { "035581", "035582" },
            perPaperOverride: new Dictionary<string, SittingCode> { ["035583"] = Winter }));
        Assert.False(r.Success);
        Assert.Equal(CommandError.PerPaperSittingOverrideKeyUnknown, r.Error);
    }

    [Fact]
    public async Task Add_perPaperOverride_value_cannot_match_primary()
    {
        var (handler, _, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(
            papers: new[] { "035581", "035582" },
            perPaperOverride: new Dictionary<string, SittingCode> { ["035581"] = Summer }));
        Assert.False(r.Success);
        Assert.Equal(CommandError.PerPaperSittingOverrideMatchesPrimary, r.Error);
    }

    [Fact]
    public async Task Add_perPaperOverride_accepted_when_valid()
    {
        var (handler, store, _) = Build();
        var r = await handler.HandleAsync(BagrutCmd(
            papers: new[] { "035581", "035582" },
            perPaperOverride: new Dictionary<string, SittingCode> { ["035581"] = Winter }));
        Assert.True(r.Success);

        var added = store.GetRawEvents(StudentId).OfType<ExamTargetAdded_V1>().Single();
        Assert.Equal(Winter, added.Target.PerPaperSittingOverride!["035581"]);
    }

    // ── AddQuestionPaperCommand ─────────────────────────────────────────

    [Fact]
    public async Task AddQuestionPaper_on_Bagrut_target_succeeds()
    {
        var (handler, store, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));
        Assert.True(add.Success);

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId!.Value, "035582"));

        Assert.True(r.Success);
        Assert.Single(store.GetRawEvents(StudentId).OfType<QuestionPaperAdded_V1>());
    }

    [Fact]
    public async Task AddQuestionPaper_rejects_already_present()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId!.Value, "035581"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodeAlreadyPresent, r.Error);
    }

    [Fact]
    public async Task AddQuestionPaper_rejects_unknown_code_via_catalog()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId!.Value, "ZZZZZZ"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodeUnknown, r.Error);
    }

    [Fact]
    public async Task AddQuestionPaper_rejects_sitting_override_matching_primary()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId!.Value, "035582", Summer));

        Assert.False(r.Success);
        Assert.Equal(CommandError.PerPaperSittingOverrideMatchesPrimary, r.Error);
    }

    [Fact]
    public async Task AddQuestionPaper_rejects_on_Standardized_target()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(PetCmd());
        Assert.True(add.Success);

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId!.Value, "anything"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodesForbidden, r.Error);
    }

    [Fact]
    public async Task AddQuestionPaper_rejects_archived_target()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));
        await handler.HandleAsync(new ArchiveExamTargetCommand(
            StudentId, add.TargetId!.Value, ArchiveReason.StudentDeclined));

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, add.TargetId.Value, "035582"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetArchived, r.Error);
    }

    // ── RemoveQuestionPaperCommand ──────────────────────────────────────

    [Fact]
    public async Task RemoveQuestionPaper_succeeds_when_leaving_at_least_one()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(
            papers: new[] { "035581", "035582" }));

        var r = await handler.HandleAsync(
            new RemoveQuestionPaperCommand(StudentId, add.TargetId!.Value, "035582"));

        Assert.True(r.Success);
    }

    [Fact]
    public async Task RemoveQuestionPaper_rejects_emptying_Bagrut_target()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new RemoveQuestionPaperCommand(StudentId, add.TargetId!.Value, "035581"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperRemovalLeavesEmpty, r.Error);
    }

    [Fact]
    public async Task RemoveQuestionPaper_rejects_unknown_code()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581", "035582" }));

        var r = await handler.HandleAsync(
            new RemoveQuestionPaperCommand(StudentId, add.TargetId!.Value, "NOT_ON_TARGET"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.QuestionPaperCodeNotPresent, r.Error);
    }

    // ── SetPerPaperSittingOverrideCommand ────────────────────────────────

    [Fact]
    public async Task SetPerPaperSitting_succeeds_for_valid_paper_and_different_sitting()
    {
        var (handler, store, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new SetPerPaperSittingOverrideCommand(
                StudentId, add.TargetId!.Value, "035581", Winter));

        Assert.True(r.Success);
        Assert.Single(store.GetRawEvents(StudentId).OfType<PerPaperSittingOverrideSet_V1>());
    }

    [Fact]
    public async Task SetPerPaperSitting_rejects_unknown_paper_code()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new SetPerPaperSittingOverrideCommand(
                StudentId, add.TargetId!.Value, "035999", Winter));

        Assert.False(r.Success);
        Assert.Equal(CommandError.PerPaperSittingOverrideKeyUnknown, r.Error);
    }

    [Fact]
    public async Task SetPerPaperSitting_rejects_primary_matching_override()
    {
        var (handler, _, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new SetPerPaperSittingOverrideCommand(
                StudentId, add.TargetId!.Value, "035581", Summer));

        Assert.False(r.Success);
        Assert.Equal(CommandError.PerPaperSittingOverrideMatchesPrimary, r.Error);
    }

    // ── ClearPerPaperSittingOverrideCommand ──────────────────────────────

    [Fact]
    public async Task ClearPerPaperSitting_emits_event_when_override_present()
    {
        var (handler, store, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(
            papers: new[] { "035581" },
            perPaperOverride: new Dictionary<string, SittingCode> { ["035581"] = Winter }));

        var r = await handler.HandleAsync(
            new ClearPerPaperSittingOverrideCommand(
                StudentId, add.TargetId!.Value, "035581"));

        Assert.True(r.Success);
        Assert.Single(store.GetRawEvents(StudentId).OfType<PerPaperSittingOverrideCleared_V1>());
    }

    [Fact]
    public async Task ClearPerPaperSitting_is_idempotent_on_missing_override()
    {
        var (handler, store, _) = Build();
        var add = await handler.HandleAsync(BagrutCmd(papers: new[] { "035581" }));

        var r = await handler.HandleAsync(
            new ClearPerPaperSittingOverrideCommand(
                StudentId, add.TargetId!.Value, "035581"));

        Assert.True(r.Success);
        // Idempotent: no event emitted.
        Assert.Empty(store.GetRawEvents(StudentId).OfType<PerPaperSittingOverrideCleared_V1>());
    }

    // ── Common TargetNotFound / TargetArchived paths ────────────────────

    [Fact]
    public async Task AddQuestionPaper_returns_not_found_for_unknown_target()
    {
        var (handler, _, _) = Build();

        var r = await handler.HandleAsync(
            new AddQuestionPaperCommand(StudentId, new ExamTargetId("et-nope"), "035581"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    [Fact]
    public async Task RemoveQuestionPaper_returns_not_found_for_unknown_target()
    {
        var (handler, _, _) = Build();

        var r = await handler.HandleAsync(
            new RemoveQuestionPaperCommand(StudentId, new ExamTargetId("et-nope"), "035581"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    [Fact]
    public async Task SetPerPaperSitting_returns_not_found_for_unknown_target()
    {
        var (handler, _, _) = Build();

        var r = await handler.HandleAsync(
            new SetPerPaperSittingOverrideCommand(
                StudentId, new ExamTargetId("et-nope"), "035581", Winter));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }

    [Fact]
    public async Task ClearPerPaperSitting_returns_not_found_for_unknown_target()
    {
        var (handler, _, _) = Build();

        var r = await handler.HandleAsync(
            new ClearPerPaperSittingOverrideCommand(
                StudentId, new ExamTargetId("et-nope"), "035581"));

        Assert.False(r.Success);
        Assert.Equal(CommandError.TargetNotFound, r.Error);
    }
}
