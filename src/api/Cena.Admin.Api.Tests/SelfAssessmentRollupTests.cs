// =============================================================================
// Cena Platform — SelfAssessmentRollup aggregation tests (RDY-057b)
//
// Exercises SelfAssessmentRollupEndpoints.BuildRollup (internal static).
// Locks the privacy contract: no student ids flow into the response, no
// free-text, no per-student rows. Only aggregate counts and top-N tags.
// =============================================================================

using Cena.Admin.Api.SelfAssessmentRollup;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests;

public class SelfAssessmentRollupAggregationTests
{
    private static OnboardingSelfAssessmentDocument Doc(
        string studentId,
        Dictionary<string, int>? confidence = null,
        List<string>? strengths = null,
        List<string>? friction = null,
        Dictionary<string, TopicFeeling>? feelings = null,
        bool skipped = false,
        string? freeText = null) => new()
        {
            Id = studentId,
            StudentId = studentId,
            CapturedAt = DateTimeOffset.UtcNow,
            Skipped = skipped,
            SubjectConfidence = confidence ?? new(),
            Strengths = strengths ?? new(),
            FrictionPoints = friction ?? new(),
            TopicFeelings = feelings ?? new(),
            FreeText = freeText,
        };

    [Fact]
    public void EmptyInput_ReturnsZeroes()
    {
        var r = SelfAssessmentRollupEndpoints.BuildRollup(
            "c-1", classroomSize: 20, docs: Array.Empty<OnboardingSelfAssessmentDocument>());

        Assert.Equal(20, r.ClassroomSize);
        Assert.Equal(0, r.RespondentCount);
        Assert.Equal(0, r.SkippedCount);
        Assert.Empty(r.ConfidenceHistogram);
        Assert.Empty(r.TopStrengthTags);
    }

    [Fact]
    public void SkippedRespondents_CountedInSkippedNotInAggregates()
    {
        var docs = new[]
        {
            Doc("s1", skipped: true),
            Doc("s2", confidence: new() { ["algebra"] = 4 }),
        };

        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 20, docs);

        Assert.Equal(2, r.RespondentCount);
        Assert.Equal(1, r.SkippedCount);
        // Skipped doc's empty SubjectConfidence must not leak.
        Assert.Contains("algebra", r.ConfidenceHistogram.Keys);
        Assert.Equal(new List<int> { 0, 0, 0, 1, 0 }, r.ConfidenceHistogram["algebra"]);
    }

    [Fact]
    public void ConfidenceHistogram_BucketsByLikertScore()
    {
        var docs = new[]
        {
            Doc("s1", confidence: new() { ["algebra"] = 1 }),
            Doc("s2", confidence: new() { ["algebra"] = 3 }),
            Doc("s3", confidence: new() { ["algebra"] = 3 }),
            Doc("s4", confidence: new() { ["algebra"] = 5 }),
        };
        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 10, docs);

        var hist = r.ConfidenceHistogram["algebra"];
        // buckets ordered 1..5
        Assert.Equal(new List<int> { 1, 0, 2, 0, 1 }, hist);
    }

    [Fact]
    public void ConfidenceScore_OutOfRange_Ignored()
    {
        var docs = new[]
        {
            Doc("s1", confidence: new() { ["algebra"] = 6 }),    // invalid
            Doc("s2", confidence: new() { ["algebra"] = -1 }),   // invalid
            Doc("s3", confidence: new() { ["algebra"] = 3 }),    // valid
        };
        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 10, docs);

        var hist = r.ConfidenceHistogram["algebra"];
        Assert.Equal(new List<int> { 0, 0, 1, 0, 0 }, hist);
    }

    [Fact]
    public void TopStrengthTags_RankedByFrequency_Top8()
    {
        var docs = new[]
        {
            Doc("s1", strengths: new() { "visualizer", "step-by-step" }),
            Doc("s2", strengths: new() { "visualizer" }),
            Doc("s3", strengths: new() { "visualizer", "enjoys-proofs" }),
        };
        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 10, docs);

        Assert.Equal("visualizer", r.TopStrengthTags[0].Tag);
        Assert.Equal(3, r.TopStrengthTags[0].Count);
    }

    [Fact]
    public void TopicFeelings_Bucketed_All4Categories()
    {
        var docs = new[]
        {
            Doc("s1", feelings: new() { ["algebra"] = TopicFeeling.Anxious }),
            Doc("s2", feelings: new() { ["algebra"] = TopicFeeling.Anxious }),
            Doc("s3", feelings: new() { ["algebra"] = TopicFeeling.Solid }),
        };
        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 10, docs);

        var algebra = r.TopicFeelingHistogram["algebra"];
        Assert.Equal(2, algebra["Anxious"]);
        Assert.Equal(1, algebra["Solid"]);
        Assert.Equal(0, algebra["Unsure"]);
        Assert.Equal(0, algebra["New"]);
    }

    [Fact]
    public void Response_NeverContainsStudentId_OrFreeText()
    {
        var docs = new[]
        {
            Doc("student-super-secret-id-42",
                confidence: new() { ["algebra"] = 3 },
                strengths: new() { "visualizer" },
                freeText: "I am a unique student with unique free text that should NEVER appear"),
        };
        var r = SelfAssessmentRollupEndpoints.BuildRollup("c-1", 10, docs);

        var json = System.Text.Json.JsonSerializer.Serialize(r);
        Assert.DoesNotContain("student-super-secret-id-42", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unique student with unique free text", json, StringComparison.Ordinal);
    }
}
