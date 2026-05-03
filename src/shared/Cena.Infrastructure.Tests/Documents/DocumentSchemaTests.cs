// =============================================================================
// Cena Platform — Document Schema Round-Trip Tests (TENANCY-P1a)
// Verifies that new tenancy document types serialize and deserialize cleanly
// with System.Text.Json (Marten's configured serializer).
// =============================================================================

using System.Text.Json;
using Cena.Infrastructure.Documents;

namespace Cena.Infrastructure.Tests.Documents;

public sealed class DocumentSchemaTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void InstituteDocument_RoundTrip_Succeeds()
    {
        var original = new InstituteDocument
        {
            Id = "institute-001",
            InstituteId = "institute-001",
            Name = "Test Academy",
            Type = InstituteType.School,
            Country = "IL",
            MentorId = "mentor-001",
            CreatedAt = new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<InstituteDocument>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Country, deserialized.Country);
        Assert.Equal(original.MentorId, deserialized.MentorId);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void InstituteDocument_DefaultValues_AreValid()
    {
        var doc = new InstituteDocument();

        Assert.NotNull(doc.Id);
        Assert.NotNull(doc.InstituteId);
        Assert.NotNull(doc.Name);
        Assert.Equal(InstituteType.School, doc.Type);
        Assert.NotNull(doc.Country);
        Assert.NotNull(doc.MentorId);
        Assert.NotEqual(default, doc.CreatedAt);
    }

    [Fact]
    public void CurriculumTrackDocument_RoundTrip_Succeeds()
    {
        var original = new CurriculumTrackDocument
        {
            Id = "track-math-001",
            TrackId = "track-math-001",
            Code = "MATH-BAGRUT-5UNIT",
            Title = "Bagrut Mathematics 5 Units",
            Subject = "math",
            TargetExam = "bagrut",
            LearningObjectiveIds = new[] { "lo-math-001", "lo-math-002" },
            StandardMappings = new[] { "bagrut:math-5u", "common-core:HSA.REI.B.3" },
            Status = CurriculumTrackStatus.Ready
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<CurriculumTrackDocument>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.TrackId, deserialized.TrackId);
        Assert.Equal(original.Code, deserialized.Code);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Subject, deserialized.Subject);
        Assert.Equal(original.TargetExam, deserialized.TargetExam);
        Assert.Equal(original.LearningObjectiveIds, deserialized.LearningObjectiveIds);
        Assert.Equal(original.StandardMappings, deserialized.StandardMappings);
        Assert.Equal(original.Status, deserialized.Status);
    }

    [Fact]
    public void CurriculumTrackDocument_DefaultStatus_IsDraft()
    {
        var doc = new CurriculumTrackDocument();

        Assert.Equal(CurriculumTrackStatus.Draft, doc.Status);
    }

    [Fact]
    public void EnrollmentDocument_RoundTrip_Succeeds()
    {
        var original = new EnrollmentDocument
        {
            Id = "enrollment-001",
            EnrollmentId = "enrollment-001",
            StudentId = "student-001",
            InstituteId = "institute-001",
            TrackId = "track-math-001",
            Status = EnrollmentStatus.Active,
            EnrolledAt = new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero),
            EndedAt = null
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<EnrollmentDocument>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal(original.EnrollmentId, deserialized.EnrollmentId);
        Assert.Equal(original.StudentId, deserialized.StudentId);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.TrackId, deserialized.TrackId);
        Assert.Equal(original.Status, deserialized.Status);
        Assert.Equal(original.EnrolledAt, deserialized.EnrolledAt);
        Assert.Null(deserialized.EndedAt);
    }

    [Fact]
    public void EnrollmentDocument_DefaultStatus_IsActive()
    {
        var doc = new EnrollmentDocument();

        Assert.Equal(EnrollmentStatus.Active, doc.Status);
    }

    [Fact]
    public void EnrollmentDocument_NullableEndedAt_Deserializes()
    {
        var original = new EnrollmentDocument
        {
            Id = "enrollment-002",
            EnrollmentId = "enrollment-002",
            StudentId = "student-002",
            InstituteId = "institute-001",
            TrackId = "track-math-001",
            Status = EnrollmentStatus.Completed,
            EnrolledAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<EnrollmentDocument>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.EndedAt, deserialized.EndedAt);
    }
}
