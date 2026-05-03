// =============================================================================
// Tests for FIND-pedagogy-008: LearningObjective metadata on Question model.
//
// Covers:
//   - Event upcaster correctly transforms V1 → V2 with null LearningObjectiveId
//     so legacy streams replay cleanly (Wiggins & McTighe 2005 backward-design
//     traceability cannot be retrofitted, so null is the only safe default).
//   - QuestionState.Apply picks up LearningObjectiveId from V2 creation events.
//   - QuestionState.Apply handles LearningObjectiveAssigned_V1 backfill.
//   - QuestionListProjection write-model carries the LO id through V2.
//   - LearningObjectiveDocument pedagogical metadata follows Anderson &
//     Krathwohl (2001) 2-axis matrix.
//   - LearningObjectiveSeedData.PickBestObjectiveId picks a plausible LO.
//   - DTO serialization carries LearningObjectiveId / LearningObjectiveTitle.
//
// Pedagogical citations:
//   - Wiggins, G. & McTighe, J. (2005). "Understanding by Design" 2nd ed.
//     ASCD. ISBN 978-1416600350.
//   - Anderson, L.W. & Krathwohl, D.R. (Eds.) (2001). "A Taxonomy for
//     Learning, Teaching, and Assessing." Pearson. ISBN 978-0321084057.
//   - Biggs, J. (2003). "Aligning Teaching for Constructing Learning."
//     Higher Education Academy.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Configuration;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Seed;

namespace Cena.Admin.Api.Tests;

public class LearningObjectiveTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly IReadOnlyList<QuestionOptionData> SampleOptions = new[]
    {
        new QuestionOptionData("A", "4", "<p>4</p>", true, null),
        new QuestionOptionData("B", "8", "<p>8</p>", false, "Adds instead of subtracting"),
    };

    // ── V1 → V2 upcaster (QuestionAuthored) ────────────────────────────────

    [Fact]
    public void QuestionAuthoredV1ToV2Upcaster_SetsNullLearningObjectiveId()
    {
        var v1 = new QuestionAuthored_V1(
            QuestionId: "q-legacy-1",
            Stem: "Solve: 2x + 6 = 14",
            StemHtml: "<p>Solve: 2x + 6 = 14</p>",
            Options: SampleOptions,
            Subject: "Math",
            Topic: "Linear Equations",
            Grade: "3 Units",
            BloomsLevel: 3,
            Difficulty: 0.4f,
            ConceptIds: new[] { "linear-equations" },
            Language: "he",
            AuthorId: "teacher-1",
            Timestamp: Now,
            Explanation: "Subtract 6, divide by 2.");

        var v2 = QuestionAuthoredV1ToV2Upcaster.Instance.Transform(v1);

        Assert.Equal(v1.QuestionId, v2.QuestionId);
        Assert.Equal(v1.Stem, v2.Stem);
        Assert.Equal(v1.Subject, v2.Subject);
        Assert.Equal(v1.BloomsLevel, v2.BloomsLevel);
        Assert.Equal(v1.Explanation, v2.Explanation);
        Assert.Null(v2.LearningObjectiveId); // V1 had no LO → null
    }

    [Fact]
    public void QuestionIngestedV1ToV2Upcaster_PreservesProvenance_AndNullsLo()
    {
        var v1 = new QuestionIngested_V1(
            QuestionId: "q-legacy-2",
            Stem: "Stem",
            StemHtml: "<p>Stem</p>",
            Options: SampleOptions,
            Subject: "Physics",
            Topic: "Kinematics",
            Grade: "3 Units",
            BloomsLevel: 2,
            Difficulty: 0.3f,
            ConceptIds: new[] { "kinematics" },
            Language: "en",
            SourceDocId: "doc-123",
            SourceUrl: "https://source.com",
            SourceFilename: "exam.pdf",
            OriginalText: "OCR text",
            ImportedBy: "importer-1",
            Timestamp: Now,
            Explanation: "v-nought plus at");

        var v2 = QuestionIngestedV1ToV2Upcaster.Instance.Transform(v1);

        Assert.Equal(v1.SourceDocId, v2.SourceDocId);
        Assert.Equal(v1.SourceUrl, v2.SourceUrl);
        Assert.Equal(v1.OriginalText, v2.OriginalText);
        Assert.Equal(v1.Explanation, v2.Explanation);
        Assert.Null(v2.LearningObjectiveId);
    }

    [Fact]
    public void QuestionAiGeneratedV1ToV2Upcaster_PreservesModelInfo_AndNullsLo()
    {
        var v1 = new QuestionAiGenerated_V1(
            QuestionId: "q-legacy-3",
            Stem: "AI stem",
            StemHtml: "<p>AI</p>",
            Options: SampleOptions,
            Subject: "Chemistry",
            Topic: "Acids",
            Grade: "5 Units",
            BloomsLevel: 4,
            Difficulty: 0.7f,
            ConceptIds: new[] { "acids-bases" },
            Language: "ar",
            PromptText: "Generate a question about acids",
            ModelId: "claude-sonnet-4-6",
            ModelTemperature: 0.7f,
            RawModelOutput: "Full LLM output here",
            RequestedBy: "user-1",
            Explanation: "Acids donate protons (H+) to bases in solution.",
            Timestamp: Now);

        var v2 = QuestionAiGeneratedV1ToV2Upcaster.Instance.Transform(v1);

        Assert.Equal(v1.PromptText, v2.PromptText);
        Assert.Equal(v1.ModelId, v2.ModelId);
        Assert.Equal(v1.ModelTemperature, v2.ModelTemperature);
        Assert.Equal(v1.Explanation, v2.Explanation);
        Assert.Null(v2.LearningObjectiveId);
    }

    // ── QuestionState.Apply with V2 creation events ─────────────────────────

    [Fact]
    public void Apply_QuestionAuthoredV2_CarriesLearningObjectiveId()
    {
        var state = new QuestionState();
        state.Apply(new QuestionAuthored_V2(
            "q-001", "Solve: 2x = 10", "<p>Solve: 2x = 10</p>",
            SampleOptions, "Math", "Linear Equations", "3 Units", 3, 0.4f,
            new[] { "linear-equations" }, "he", "teacher-1", Now,
            Explanation: "Divide by 2.",
            LearningObjectiveId: "lo-math-alg-linear-001"));

        Assert.Equal("lo-math-alg-linear-001", state.LearningObjectiveId);
        Assert.Equal("Math", state.Subject);
        Assert.Equal(1, state.EventVersion);
    }

    [Fact]
    public void Apply_QuestionAuthoredV1_LegacyEventReplaysWithNullLo()
    {
        // Simulates a V1 event replaying on cold boot. The V1 Apply overload
        // delegates to V2 via the internal upcasting path, so
        // LearningObjectiveId should be null afterwards.
        var state = new QuestionState();
        state.Apply(new QuestionAuthored_V1(
            "q-old", "Legacy stem", "<p>Legacy</p>",
            SampleOptions, "Math", "Algebra", "3 Units", 3, 0.4f,
            new[] { "algebra" }, "he", "teacher-x", Now));

        Assert.Null(state.LearningObjectiveId);
        Assert.Equal("Legacy stem", state.Stem);
    }

    [Fact]
    public void Apply_LearningObjectiveAssigned_BackfillsExistingQuestion()
    {
        var state = new QuestionState();
        // Create from legacy V1 (no LO)
        state.Apply(new QuestionAuthored_V1(
            "q-100", "Stem", "<p>Stem</p>",
            SampleOptions, "Math", "Algebra", "3 Units", 3, 0.4f,
            new[] { "linear-equations" }, "he", "teacher-y", Now));

        Assert.Null(state.LearningObjectiveId);
        Assert.Equal(1, state.EventVersion);

        // Curriculum author backfills the LO id
        state.Apply(new LearningObjectiveAssigned_V1(
            QuestionId: "q-100",
            PreviousObjectiveId: null,
            NewObjectiveId: "lo-math-alg-linear-001",
            AssignedBy: "curriculum-author",
            Timestamp: Now.AddMinutes(5)));

        Assert.Equal("lo-math-alg-linear-001", state.LearningObjectiveId);
        Assert.Equal(2, state.EventVersion);
        Assert.NotNull(state.UpdatedAt);
    }

    [Fact]
    public void Apply_LearningObjectiveAssigned_OverridesExistingLo()
    {
        var state = new QuestionState();
        state.Apply(new QuestionAuthored_V2(
            "q-101", "Stem", "<p>Stem</p>",
            SampleOptions, "Math", "Calculus", "5 Units", 3, 0.6f,
            new[] { "derivatives" }, "he", "teacher-z", Now,
            Explanation: null,
            LearningObjectiveId: "lo-math-calc-derivatives-001"));

        state.Apply(new LearningObjectiveAssigned_V1(
            "q-101", "lo-math-calc-derivatives-001", "lo-math-calc-integrals-001",
            "curriculum-author", Now.AddMinutes(5)));

        Assert.Equal("lo-math-calc-integrals-001", state.LearningObjectiveId);
        Assert.Equal(2, state.EventVersion);
    }

    // ── QuestionListProjection ──────────────────────────────────────────────

    [Fact]
    public void QuestionListProjection_CreateFromV2_SetsLearningObjectiveId()
    {
        var projection = new QuestionListProjection();
        var model = projection.Create(new QuestionAuthored_V2(
            "q-200", "Stem", "<p>Stem</p>",
            SampleOptions, "Math", "Linear Equations", "3 Units", 3, 0.4f,
            new[] { "linear-equations" }, "he", "teacher-1", Now,
            Explanation: null,
            LearningObjectiveId: "lo-math-alg-linear-001"));

        Assert.Equal("q-200", model.Id);
        Assert.Equal("lo-math-alg-linear-001", model.LearningObjectiveId);
    }

    [Fact]
    public void QuestionListProjection_CreateFromLegacyV1_ReplaysWithNullLo()
    {
        var projection = new QuestionListProjection();
        var model = projection.Create(new QuestionAuthored_V1(
            "q-201", "Stem", "<p>Stem</p>",
            SampleOptions, "Math", "Linear Equations", "3 Units", 3, 0.4f,
            new[] { "linear-equations" }, "he", "teacher-1", Now));

        Assert.Null(model.LearningObjectiveId);
    }

    [Fact]
    public void QuestionListProjection_ApplyLearningObjectiveAssigned_BackfillsReadModel()
    {
        var projection = new QuestionListProjection();
        var model = projection.Create(new QuestionAuthored_V1(
            "q-202", "Stem", "<p>Stem</p>",
            SampleOptions, "Math", "Linear Equations", "3 Units", 3, 0.4f,
            new[] { "linear-equations" }, "he", "teacher-1", Now));

        Assert.Null(model.LearningObjectiveId);

        projection.Apply(new LearningObjectiveAssigned_V1(
            "q-202", null, "lo-math-alg-linear-001",
            "curriculum-author", Now.AddMinutes(10)), model);

        Assert.Equal("lo-math-alg-linear-001", model.LearningObjectiveId);
        Assert.NotNull(model.UpdatedAt);
    }

    // ── LearningObjectiveDocument and enums ────────────────────────────────

    [Fact]
    public void LearningObjectiveDocument_BloomsLevelProjectionMatchesCognitiveProcess()
    {
        // Anderson & Krathwohl 2001: Apply is L3 on the 6-level cognitive-process axis.
        var doc = new LearningObjectiveDocument
        {
            Id = "lo-test-1",
            Code = "TEST-001",
            Title = "Test",
            Subject = "Math",
            CognitiveProcess = CognitiveProcess.Apply,
            KnowledgeType = KnowledgeType.Procedural,
        };

        Assert.Equal(3, doc.BloomsLevel);
    }

    [Fact]
    public void CognitiveProcess_EnumValuesMatchBloomsAxis()
    {
        // The revised Bloom's matrix orders cognitive-process levels 1..6.
        Assert.Equal(1, (int)CognitiveProcess.Remember);
        Assert.Equal(2, (int)CognitiveProcess.Understand);
        Assert.Equal(3, (int)CognitiveProcess.Apply);
        Assert.Equal(4, (int)CognitiveProcess.Analyze);
        Assert.Equal(5, (int)CognitiveProcess.Evaluate);
        Assert.Equal(6, (int)CognitiveProcess.Create);
    }

    [Fact]
    public void KnowledgeType_EnumValuesCoverFourDimensions()
    {
        // Anderson & Krathwohl 2001 horizontal axis: factual / conceptual /
        // procedural / metacognitive.
        Assert.Equal(4, Enum.GetValues<KnowledgeType>().Length);
        Assert.True(Enum.IsDefined(typeof(KnowledgeType), KnowledgeType.Factual));
        Assert.True(Enum.IsDefined(typeof(KnowledgeType), KnowledgeType.Conceptual));
        Assert.True(Enum.IsDefined(typeof(KnowledgeType), KnowledgeType.Procedural));
        Assert.True(Enum.IsDefined(typeof(KnowledgeType), KnowledgeType.Metacognitive));
    }

    // ── Seed data (LO picker and match logic) ──────────────────────────────

    [Fact]
    public void SeedObjectives_CoverAllSeedSubjects()
    {
        var objectives = LearningObjectiveSeedData.GetSeedObjectives();

        // The seed set must include at least one LO per major subject so
        // QuestionBankSeedData backfill can pick one for every seeded question.
        var subjects = objectives.Select(o => o.Subject).ToHashSet();
        Assert.Contains("Math", subjects);
        Assert.Contains("Physics", subjects);
        Assert.Contains("Chemistry", subjects);
        Assert.Contains("Biology", subjects);
        Assert.Contains("Computer Science", subjects);
        Assert.Contains("English", subjects);
        Assert.True(objectives.Count >= 5, "Expected at least 5 seed objectives");
    }

    [Fact]
    public void SeedObjectives_AllHaveNonEmptyCodeTitleAndStandards()
    {
        foreach (var obj in LearningObjectiveSeedData.GetSeedObjectives())
        {
            Assert.False(string.IsNullOrWhiteSpace(obj.Id), $"id missing for {obj.Code}");
            Assert.False(string.IsNullOrWhiteSpace(obj.Code), $"code missing for {obj.Id}");
            Assert.False(string.IsNullOrWhiteSpace(obj.Title), $"title missing for {obj.Id}");
            Assert.False(string.IsNullOrWhiteSpace(obj.Description), $"description missing for {obj.Id}");
            Assert.NotEmpty(obj.StandardsAlignment);
        }
    }

    [Fact]
    public void PickBestObjectiveId_PrefersSameSubjectConceptOverlap()
    {
        // Subject=Math, Concepts={linear-equations} should match
        // the linear-equations LO.
        var picked = LearningObjectiveSeedData.PickBestObjectiveId(
            "Math", new[] { "linear-equations" });
        Assert.Equal("lo-math-alg-linear-001", picked);
    }

    [Fact]
    public void PickBestObjectiveId_FallsBackToSubjectWhenNoConceptMatch()
    {
        // Obscure concept that isn't in any seed LO — still return a Math LO
        // rather than null.
        var picked = LearningObjectiveSeedData.PickBestObjectiveId(
            "Math", new[] { "unrelated-concept-zzzz" });
        Assert.NotNull(picked);
        Assert.StartsWith("lo-math", picked);
    }

    [Fact]
    public void PickBestObjectiveId_ReturnsNullForUnknownSubjectAndConcepts()
    {
        var picked = LearningObjectiveSeedData.PickBestObjectiveId(
            "UnknownSubject", new[] { "nothing" });
        Assert.Null(picked);
    }

    [Fact]
    public void PickBestObjectiveId_PhysicsKinematicsMatchesKinematicsLo()
    {
        var picked = LearningObjectiveSeedData.PickBestObjectiveId(
            "Physics", new[] { "kinematics", "motion" });
        Assert.Equal("lo-physics-mech-kinematics-001", picked);
    }

    // ── DTO serialization ──────────────────────────────────────────────────

    [Fact]
    public void QuestionBankDetailResponse_SerializesLearningObjectiveFields()
    {
        var detail = new QuestionBankDetailResponse(
            Id: "q-300",
            Stem: "Stem",
            StemHtml: "<p>Stem</p>",
            Options: Array.Empty<AnswerOptionDetail>(),
            CorrectAnswers: Array.Empty<string>(),
            Subject: "Math",
            Topic: "Linear Equations",
            Grade: "3 Units",
            BloomsLevel: 3,
            Difficulty: 0.4f,
            ConceptIds: new[] { "linear-equations" },
            ConceptNames: new[] { "Linear Equations" },
            Status: QuestionStatus.Draft,
            QualityScore: 80,
            SourceType: "authored",
            SourceItemId: null,
            CreatedAt: Now,
            UpdatedAt: null,
            CreatedBy: "teacher-1",
            Explanation: null,
            Performance: null,
            Provenance: null,
            QualityGate: null,
            LearningObjectiveId: "lo-math-alg-linear-001",
            LearningObjectiveTitle: "Solve single-variable linear equations");

        var json = JsonSerializer.Serialize(detail);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(
            "lo-math-alg-linear-001",
            doc.RootElement.GetProperty("LearningObjectiveId").GetString());
        Assert.Equal(
            "Solve single-variable linear equations",
            doc.RootElement.GetProperty("LearningObjectiveTitle").GetString());
    }

    [Fact]
    public void LearningObjectiveListResponse_SerializesCognitiveProcessAndKnowledgeType()
    {
        var item = new LearningObjectiveListItem(
            Id: "lo-1",
            Code: "MATH-001",
            Title: "Solve linear equations",
            Description: "SWBAT…",
            Subject: "Math",
            Grade: "3 Units",
            CognitiveProcess: "Apply",
            KnowledgeType: "Procedural",
            BloomsLevel: 3,
            ConceptIds: new[] { "linear-equations" },
            StandardsAlignment: new Dictionary<string, string> { ["bagrut"] = "X" });

        var response = new LearningObjectiveListResponse(new[] { item }, 1);
        var json = JsonSerializer.Serialize(response);

        Assert.Contains("\"Apply\"", json);
        Assert.Contains("\"Procedural\"", json);
        Assert.Contains("\"bagrut\"", json);
    }

    // ── Domain model (LearningObjective record) ────────────────────────────

    [Fact]
    public void LearningObjective_FromDocument_CarriesAllFields()
    {
        var doc = new LearningObjectiveDocument
        {
            Id = "lo-test-99",
            Code = "TEST-099",
            Title = "Test LO",
            Description = "Students will do X.",
            Subject = "Math",
            Grade = "3 Units",
            CognitiveProcess = CognitiveProcess.Analyze,
            KnowledgeType = KnowledgeType.Conceptual,
            ConceptIds = new List<string> { "c1", "c2" },
            StandardsAlignment = new Dictionary<string, string> { ["bagrut"] = "X" },
            CreatedAt = Now,
            IsActive = true,
        };

        var lo = LearningObjective.FromDocument(doc);

        Assert.Equal("lo-test-99", lo.Id);
        Assert.Equal("Test LO", lo.Title);
        Assert.Equal(CognitiveProcess.Analyze, lo.CognitiveProcess);
        Assert.Equal(KnowledgeType.Conceptual, lo.KnowledgeType);
        Assert.Equal(4, lo.BloomsLevel); // Analyze = 4
        Assert.Equal(2, lo.ConceptIds.Count);
        Assert.True(lo.IsActive);
    }

    [Fact]
    public void BloomsClassification_FromLegacyLevel_DefaultsToConceptualKnowledge()
    {
        var cls = BloomsClassification.FromLegacyLevel(3);
        Assert.Equal(CognitiveProcess.Apply, cls.CognitiveProcess);
        Assert.Equal(KnowledgeType.Conceptual, cls.KnowledgeType);
        Assert.Equal(3, cls.Level);
    }

    [Fact]
    public void BloomsClassification_FromLegacyLevel_ClampsOutOfRangeValues()
    {
        Assert.Equal(CognitiveProcess.Remember, BloomsClassification.FromLegacyLevel(0).CognitiveProcess);
        Assert.Equal(CognitiveProcess.Create, BloomsClassification.FromLegacyLevel(10).CognitiveProcess);
    }

    // ── QuestionDocument (Infrastructure shape) ────────────────────────────

    [Fact]
    public void QuestionDocument_DefaultsToNullLearningObjectiveId()
    {
        var doc = new QuestionDocument();
        Assert.Null(doc.LearningObjectiveId);
    }

    [Fact]
    public void QuestionDocument_CanCarryLearningObjectiveId()
    {
        var doc = new QuestionDocument
        {
            Id = "seed:question:math:001",
            Subject = "Mathematics",
            ConceptId = "concept:math:linear-equations",
            Prompt = "Solve for x",
            CorrectAnswer = "5",
            LearningObjectiveId = "lo-math-alg-linear-001",
        };

        Assert.Equal("lo-math-alg-linear-001", doc.LearningObjectiveId);
    }
}
