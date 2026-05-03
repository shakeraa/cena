// =============================================================================
// Cena Platform — Enrollment Event Registration Tests (TENANCY-P1c)
//
// Verifies that the 8 enrollment lifecycle events are properly registered
// with Marten under snake_case_v1 aliases and can be serialized/deserialized.
// =============================================================================

using System.Text.Json;
using System.Text.RegularExpressions;
using Cena.Actors.Configuration;
using Cena.Actors.Events;
using Marten;

namespace Cena.Actors.Tests.Events;

public sealed class EnrollmentEventRegistrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DocumentStore _store;

    public EnrollmentEventRegistrationTests()
    {
        _store = DocumentStore.For(opts =>
            opts.ConfigureCenaEventStore("Host=localhost;Database=test;Username=test;Password=test"));
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public void AllEightEvents_AreRegistered_InMarten()
    {
        Assert.Equal("institute_created_v1", _store.Events.EventMappingFor(typeof(InstituteCreated_V1)).Alias);
        Assert.Equal("curriculum_track_published_v1", _store.Events.EventMappingFor(typeof(CurriculumTrackPublished_V1)).Alias);
        Assert.Equal("program_created_v1", _store.Events.EventMappingFor(typeof(ProgramCreated_V1)).Alias);
        Assert.Equal("program_forked_from_platform_v1", _store.Events.EventMappingFor(typeof(ProgramForkedFromPlatform_V1)).Alias);
        Assert.Equal("classroom_created_v1", _store.Events.EventMappingFor(typeof(ClassroomCreated_V1)).Alias);
        Assert.Equal("classroom_status_changed_v1", _store.Events.EventMappingFor(typeof(ClassroomStatusChanged_V1)).Alias);
        Assert.Equal("enrollment_created_v1", _store.Events.EventMappingFor(typeof(EnrollmentCreated_V1)).Alias);
        Assert.Equal("enrollment_status_changed_v1", _store.Events.EventMappingFor(typeof(EnrollmentStatusChanged_V1)).Alias);
    }

    [Fact]
    public void EventNames_AreSnakeCaseV1()
    {
        var aliases = new[]
        {
            _store.Events.EventMappingFor(typeof(InstituteCreated_V1)).Alias,
            _store.Events.EventMappingFor(typeof(CurriculumTrackPublished_V1)).Alias,
            _store.Events.EventMappingFor(typeof(ProgramCreated_V1)).Alias,
            _store.Events.EventMappingFor(typeof(ProgramForkedFromPlatform_V1)).Alias,
            _store.Events.EventMappingFor(typeof(ClassroomCreated_V1)).Alias,
            _store.Events.EventMappingFor(typeof(ClassroomStatusChanged_V1)).Alias,
            _store.Events.EventMappingFor(typeof(EnrollmentCreated_V1)).Alias,
            _store.Events.EventMappingFor(typeof(EnrollmentStatusChanged_V1)).Alias
        };

        var regex = new Regex("^[a-z_]+_v1$");
        Assert.All(aliases, alias =>
        {
            Assert.Matches(regex, alias);
        });
    }

    [Fact]
    public void InstituteCreated_V1_Serializes_RoundTrip()
    {
        var original = new InstituteCreated_V1(
            InstituteId: "institute-001",
            Type: "School",
            Name: "Test Academy",
            Country: "IL",
            MentorId: "mentor-001",
            CreatedAt: new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero)
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<InstituteCreated_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.Type, deserialized.Type);
        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Country, deserialized.Country);
        Assert.Equal(original.MentorId, deserialized.MentorId);
        Assert.Equal(original.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void CurriculumTrackPublished_V1_Serializes_RoundTrip()
    {
        var original = new CurriculumTrackPublished_V1(
            TrackId: "track-math-001",
            Code: "MATH-BAGRUT-5UNIT",
            Title: "Bagrut Mathematics 5 Units",
            Subject: "math",
            TargetExam: "bagrut",
            LearningObjectiveIds: new[] { "lo-math-001", "lo-math-002" }
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<CurriculumTrackPublished_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.TrackId, deserialized.TrackId);
        Assert.Equal(original.Code, deserialized.Code);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Subject, deserialized.Subject);
        Assert.Equal(original.TargetExam, deserialized.TargetExam);
        Assert.Equal(original.LearningObjectiveIds, deserialized.LearningObjectiveIds);
    }

    [Fact]
    public void ProgramCreated_V1_Serializes_RoundTrip()
    {
        var original = new ProgramCreated_V1(
            ProgramId: "program-001",
            InstituteId: "institute-001",
            TrackId: "track-math-001",
            Title: "Advanced Math Program",
            Origin: "Custom",
            ParentProgramId: null,
            ContentPackVersion: "1.0.0",
            CreatedByMentorId: "mentor-001"
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<ProgramCreated_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ProgramId, deserialized.ProgramId);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.TrackId, deserialized.TrackId);
        Assert.Equal(original.Title, deserialized.Title);
        Assert.Equal(original.Origin, deserialized.Origin);
        Assert.Null(deserialized.ParentProgramId);
        Assert.Equal(original.ContentPackVersion, deserialized.ContentPackVersion);
        Assert.Equal(original.CreatedByMentorId, deserialized.CreatedByMentorId);
    }

    [Fact]
    public void ProgramForkedFromPlatform_V1_Serializes_RoundTrip()
    {
        var original = new ProgramForkedFromPlatform_V1(
            NewProgramId: "program-fork-001",
            ParentProgramId: "program-platform-001",
            InstituteId: "institute-001",
            ForkedByMentorId: "mentor-001"
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<ProgramForkedFromPlatform_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.NewProgramId, deserialized.NewProgramId);
        Assert.Equal(original.ParentProgramId, deserialized.ParentProgramId);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.ForkedByMentorId, deserialized.ForkedByMentorId);
    }

    [Fact]
    public void ClassroomCreated_V1_Serializes_RoundTrip()
    {
        var original = new ClassroomCreated_V1(
            ClassroomId: "classroom-001",
            InstituteId: "institute-001",
            ProgramId: "program-001",
            Mode: "InstructorLed",
            MentorIds: new[] { "mentor-001", "mentor-002" },
            JoinApprovalMode: "ManualApprove"
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<ClassroomCreated_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ClassroomId, deserialized.ClassroomId);
        Assert.Equal(original.InstituteId, deserialized.InstituteId);
        Assert.Equal(original.ProgramId, deserialized.ProgramId);
        Assert.Equal(original.Mode, deserialized.Mode);
        Assert.Equal(original.MentorIds, deserialized.MentorIds);
        Assert.Equal(original.JoinApprovalMode, deserialized.JoinApprovalMode);
    }

    [Fact]
    public void ClassroomStatusChanged_V1_NullReason_Serializes()
    {
        var original = new ClassroomStatusChanged_V1(
            ClassroomId: "classroom-001",
            NewStatus: "Archived",
            ChangedAt: new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero),
            Reason: null
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<ClassroomStatusChanged_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ClassroomId, deserialized.ClassroomId);
        Assert.Equal(original.NewStatus, deserialized.NewStatus);
        Assert.Equal(original.ChangedAt, deserialized.ChangedAt);
        Assert.Null(deserialized.Reason);
    }

    [Fact]
    public void EnrollmentCreated_V1_Serializes_RoundTrip()
    {
        var original = new EnrollmentCreated_V1(
            EnrollmentId: "enrollment-001",
            StudentId: "student-001",
            ClassroomId: "classroom-001",
            EnrolledAt: new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero)
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<EnrollmentCreated_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.EnrollmentId, deserialized.EnrollmentId);
        Assert.Equal(original.StudentId, deserialized.StudentId);
        Assert.Equal(original.ClassroomId, deserialized.ClassroomId);
        Assert.Equal(original.EnrolledAt, deserialized.EnrolledAt);
    }

    [Fact]
    public void EnrollmentStatusChanged_V1_Serializes_RoundTrip()
    {
        var original = new EnrollmentStatusChanged_V1(
            EnrollmentId: "enrollment-001",
            NewStatus: "Paused",
            ChangedAt: new DateTimeOffset(2026, 4, 12, 10, 30, 0, TimeSpan.Zero),
            Reason: "Student requested pause"
        );

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<EnrollmentStatusChanged_V1>(json, JsonOpts);

        Assert.NotNull(deserialized);
        Assert.Equal(original.EnrollmentId, deserialized.EnrollmentId);
        Assert.Equal(original.NewStatus, deserialized.NewStatus);
        Assert.Equal(original.ChangedAt, deserialized.ChangedAt);
        Assert.Equal(original.Reason, deserialized.Reason);
    }
}
