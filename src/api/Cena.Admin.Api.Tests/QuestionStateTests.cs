// =============================================================================
// Tests for Question Aggregate State (event sourcing Apply methods)
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Questions;

namespace Cena.Admin.Api.Tests;

public class QuestionStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static QuestionAuthored_V1 MakeAuthored(string id = "q-001") => new(
        id, "Solve: 2x = 10", "<p>Solve: 2x = 10</p>",
        new[]
        {
            new QuestionOptionData("A", "5", "<p>5</p>", true, null),
            new QuestionOptionData("B", "10", "<p>10</p>", false, "Doesn't divide"),
            new QuestionOptionData("C", "2", "<p>2</p>", false, "Wrong operation"),
            new QuestionOptionData("D", "20", "<p>20</p>", false, "Multiplies instead"),
        },
        "Math", "Linear Equations", "4 Units", 3, 0.4f,
        new[] { "linear-equations" }, "he", "teacher-1", Now);

    [Fact]
    public void Apply_QuestionAuthored_SetsAllFields()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        Assert.Equal("q-001", state.Id);
        Assert.Equal("Solve: 2x = 10", state.Stem);
        Assert.Equal("Math", state.Subject);
        Assert.Equal("Linear Equations", state.Topic);
        Assert.Equal("4 Units", state.Grade);
        Assert.Equal(3, state.BloomsLevel);
        Assert.Equal(0.4f, state.Difficulty);
        Assert.Equal("he", state.PrimaryLanguage);
        Assert.Equal("authored", state.SourceType);
        Assert.Equal("teacher-1", state.CreatedBy);
        Assert.Equal(QuestionLifecycleStatus.Draft, state.Status);
        Assert.Equal(4, state.Options.Count);
        Assert.Single(state.Options, o => o.IsCorrect);
        Assert.Equal(1, state.EventVersion);
    }

    [Fact]
    public void Apply_QuestionIngested_SetsProvenance()
    {
        var state = new QuestionState();
        var evt = new QuestionIngested_V1(
            "q-002", "Stem", "<p>Stem</p>",
            new[] { new QuestionOptionData("A", "a", "<p>a</p>", true, null) },
            "Physics", "Kinematics", "3 Units", 2, 0.3f,
            new[] { "kinematics" }, "en",
            "doc-123", "https://source.com", "exam.pdf", "OCR text", "importer-1", Now);

        state.Apply(evt);

        Assert.Equal("ingested", state.SourceType);
        Assert.NotNull(state.Provenance);
        Assert.Equal("doc-123", state.Provenance!.SourceDocId);
        Assert.Equal("https://source.com", state.Provenance.SourceUrl);
        Assert.Equal("exam.pdf", state.Provenance.SourceFilename);
        Assert.Equal("OCR text", state.Provenance.OriginalText);
    }

    [Fact]
    public void Apply_QuestionAiGenerated_StoresPromptAndModel()
    {
        var state = new QuestionState();
        var evt = new QuestionAiGenerated_V1(
            "q-003", "AI question", "<p>AI</p>",
            new[] { new QuestionOptionData("A", "a", "<p>a</p>", true, null) },
            "Chemistry", "Acids", "5 Units", 4, 0.7f,
            new[] { "acids-bases" }, "ar",
            "Generate a question about acids", "claude-sonnet-4-6", 0.7f,
            "Full LLM output here", "user-1",
            "Acids donate protons (H+) to bases in solution.", Now);

        state.Apply(evt);

        Assert.Equal("ai-generated", state.SourceType);
        Assert.NotNull(state.AiProvenance);
        Assert.Equal("Generate a question about acids", state.AiProvenance!.PromptText);
        Assert.Equal("claude-sonnet-4-6", state.AiProvenance.ModelId);
        Assert.Equal(0.7f, state.AiProvenance.ModelTemperature);
        Assert.Equal("Full LLM output here", state.AiProvenance.RawModelOutput);
        Assert.Equal("Acids donate protons (H+) to bases in solution.", state.AiProvenance.Explanation);
        Assert.Equal("Acids donate protons (H+) to bases in solution.", state.Explanation);
    }

    [Fact]
    public void Apply_StemEdited_UpdatesStem()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionStemEdited_V1("q-001", "Solve: 2x = 10", "Solve: 3x = 15", "<p>Solve: 3x = 15</p>", "editor-1", Now));

        Assert.Equal("Solve: 3x = 15", state.Stem);
        Assert.NotNull(state.UpdatedAt);
        Assert.Equal(2, state.EventVersion);
    }

    [Fact]
    public void Apply_OptionChanged_UpdatesSpecificOption()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionOptionChanged_V1("q-001", "B", "10", "15", "<p>15</p>", false, "Off by one", "editor-1", Now));

        var optB = state.Options.First(o => o.Label == "B");
        Assert.Equal("15", optB.Text);
        Assert.Equal("Off by one", optB.DistractorRationale);
        Assert.False(optB.IsCorrect);
    }

    [Fact]
    public void Apply_MetadataUpdated_ChangesDifficulty()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionMetadataUpdated_V1("q-001", "difficulty", "0.4", "0.8", "editor-1", Now));

        Assert.Equal(0.8f, state.Difficulty);
    }

    [Fact]
    public void Apply_MetadataUpdated_ChangesBloomsLevel()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionMetadataUpdated_V1("q-001", "bloomsLevel", "3", "5", "editor-1", Now));

        Assert.Equal(5, state.BloomsLevel);
    }

    [Fact]
    public void Apply_QualityEvaluated_UpdatesScoreAndStatus()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionQualityEvaluated_V1("q-001", 82.5f, 80, 80, 75, 85, 90, 70, 93, 80, "NeedsReview", 1, Now));

        Assert.Equal(82, state.QualityScore); // Math.Round uses banker's rounding (82.5 → 82)
        Assert.Equal(QuestionLifecycleStatus.InReview, state.Status);
        Assert.NotNull(state.LastQualityEvaluation);
        Assert.Equal("NeedsReview", state.LastQualityEvaluation!.GateDecision);
    }

    [Fact]
    public void Apply_QualityEvaluated_AutoApproved_KeepsDraftStatus()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        // AutoApproved doesn't change status — the Approved event does that
        state.Apply(new QuestionQualityEvaluated_V1("q-001", 92f, 95, 90, 85, 88, 95, 80, 97, 85, "AutoApproved", 0, Now));

        Assert.Equal(QuestionLifecycleStatus.Draft, state.Status);
    }

    [Fact]
    public void Apply_Approved_ChangesStatusToApproved()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new QuestionApproved_V1("q-001", "admin-1", Now));

        Assert.Equal(QuestionLifecycleStatus.Approved, state.Status);
    }

    [Fact]
    public void Apply_Published_ChangesStatusToPublished()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());
        state.Apply(new QuestionApproved_V1("q-001", "admin-1", Now));
        state.Apply(new QuestionPublished_V1("q-001", "admin-1", Now));

        Assert.Equal(QuestionLifecycleStatus.Published, state.Status);
    }

    [Fact]
    public void Apply_Deprecated_ChangesStatusToDeprecated()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());
        state.Apply(new QuestionDeprecated_V1("q-001", "Low quality", true, "admin-1", Now));

        Assert.Equal(QuestionLifecycleStatus.Deprecated, state.Status);
    }

    [Fact]
    public void Apply_LanguageVersionAdded_AddsToVersions()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored());

        state.Apply(new LanguageVersionAdded_V1("q-001", "ar",
            "Arabic stem text", "<p>Arabic stem</p>",
            new[] { new QuestionOptionData("A", "ar-opt", "<p>ar</p>", true, null) },
            "translator-1", Now));

        Assert.Single(state.LanguageVersions);
        Assert.True(state.LanguageVersions.ContainsKey("ar"));
        Assert.Equal("Arabic stem text", state.LanguageVersions["ar"].Stem);
        Assert.Equal("translator-1", state.LanguageVersions["ar"].TranslatedBy);
    }

    [Fact]
    public void Apply_MultipleLanguageVersions_AllStored()
    {
        var state = new QuestionState();
        state.Apply(MakeAuthored()); // Hebrew primary

        state.Apply(new LanguageVersionAdded_V1("q-001", "ar", "Arabic", "<p>ar</p>",
            new[] { new QuestionOptionData("A", "a", "<p>a</p>", true, null) }, "t1", Now));
        state.Apply(new LanguageVersionAdded_V1("q-001", "en", "English", "<p>en</p>",
            new[] { new QuestionOptionData("A", "a", "<p>a</p>", true, null) }, "t2", Now));

        Assert.Equal(2, state.LanguageVersions.Count);
        Assert.True(state.LanguageVersions.ContainsKey("ar"));
        Assert.True(state.LanguageVersions.ContainsKey("en"));
    }

    [Fact]
    public void EventVersion_IncrementsWithEachEvent()
    {
        var state = new QuestionState();
        Assert.Equal(0, state.EventVersion);

        state.Apply(MakeAuthored());
        Assert.Equal(1, state.EventVersion);

        state.Apply(new QuestionStemEdited_V1("q-001", "old", "new", "<p>new</p>", "e", Now));
        Assert.Equal(2, state.EventVersion);

        state.Apply(new QuestionApproved_V1("q-001", "a", Now));
        Assert.Equal(3, state.EventVersion);
    }

    [Fact]
    public void FullLifecycle_AuthoredToPublished()
    {
        var state = new QuestionState();

        // Create
        state.Apply(MakeAuthored());
        Assert.Equal(QuestionLifecycleStatus.Draft, state.Status);

        // Quality gate → NeedsReview
        state.Apply(new QuestionQualityEvaluated_V1("q-001", 75f, 80, 80, 75, 70, 80, 70, 80, 80, "NeedsReview", 2, Now));
        Assert.Equal(QuestionLifecycleStatus.InReview, state.Status);

        // Human approves
        state.Apply(new QuestionApproved_V1("q-001", "reviewer-1", Now));
        Assert.Equal(QuestionLifecycleStatus.Approved, state.Status);

        // Publish
        state.Apply(new QuestionPublished_V1("q-001", "admin-1", Now));
        Assert.Equal(QuestionLifecycleStatus.Published, state.Status);

        Assert.Equal(4, state.EventVersion);
    }
}
