// =============================================================================
// Cena Platform — prr-008 negative-integration tests for the item-delivery gate.
//
// These exercise the runtime enforcement path: an attempt to deliver a
// `MinistryBagrut`-provenanced item to a student must throw, SIEM-log, and
// refuse to emit an audit event. A deliverable item (AiRecreated or
// TeacherAuthoredOriginal) must pass through unmolested.
// =============================================================================

using Microsoft.Extensions.Logging;

using Cena.Actors.Assessment;
using Cena.Actors.Content;

namespace Cena.Actors.Tests.Assessment;

public sealed class BagrutRecreationOnlyDeliveryTests
{
    private static ExamSimulationState SeedSimulation() => new()
    {
        SimulationId = "sim-42",
        StudentId = "stu-007",
        ExamCode = "806",
        Format = ExamFormat.Bagrut806,
        PartAQuestionIds = new() { "item-ministry-2024-q3a" },
        StartedAt = DateTimeOffset.UtcNow,
        VariantSeed = 12345,
    };

    [Fact]
    public void AssertDeliverable_Throws_On_MinistryBagrut_Item_And_Writes_Siem_Log()
    {
        var logger = new CapturingLogger<ItemDeliveryGate>();
        var gate = new ItemDeliveryGate(logger);
        var state = SeedSimulation();

        var ministryItem = new Provenance(
            Kind: ProvenanceKind.MinistryBagrut,
            Recorded: DateTimeOffset.UtcNow,
            Source: "Ministry code 805, Bagrut 2024 summer Q3a");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExamSimulationDelivery.AssertDeliverable(
                gate: gate,
                state: state,
                itemId: "item-ministry-2024-q3a",
                provenance: ministryItem,
                tenantId: "tenant-cena-demo",
                actorId: "StudentActor/stu-007"));

        // The exception itself must be actionable (not a generic null ref).
        Assert.Contains("reference-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BagrutRecreationAggregate", ex.Message, StringComparison.Ordinal);

        // One SIEM-grade structured log entry at Error level with the
        // pinned event id, and it must include every contextual field.
        Assert.Single(logger.Entries);
        var entry = logger.Entries[0];
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(ItemDeliveryGate.BagrutReferenceOnlyViolationEventId, entry.EventId);
        Assert.Contains("item-ministry-2024-q3a", entry.Message, StringComparison.Ordinal);
        Assert.Contains("sim-42", entry.Message, StringComparison.Ordinal);
        Assert.Contains("tenant-cena-demo", entry.Message, StringComparison.Ordinal);
        Assert.Contains("StudentActor/stu-007", entry.Message, StringComparison.Ordinal);

        // The raw item body must never appear in the log — only identifiers.
        // (We seeded the source string with the Ministry reference; the log
        //  MAY include that source since it's metadata, but MUST NOT include
        //  the stem/answer body — we never pass that to the gate, so by
        //  construction it can't leak.)
        Assert.DoesNotContain("stem", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertDeliverable_Permits_AiRecreated_Item_And_Does_Not_Log()
    {
        var logger = new CapturingLogger<ItemDeliveryGate>();
        var gate = new ItemDeliveryGate(logger);
        var state = SeedSimulation();

        var recreatedItem = new Provenance(
            Kind: ProvenanceKind.AiRecreated,
            Recorded: DateTimeOffset.UtcNow,
            Source: "BagrutRecreation/rec-9981");

        // Must not throw.
        ExamSimulationDelivery.AssertDeliverable(
            gate: gate,
            state: state,
            itemId: "item-rec-9981",
            provenance: recreatedItem,
            tenantId: "tenant-cena-demo",
            actorId: "StudentActor/stu-007");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void AssertDeliverable_Permits_TeacherAuthoredOriginal_Item_And_Does_Not_Log()
    {
        var logger = new CapturingLogger<ItemDeliveryGate>();
        var gate = new ItemDeliveryGate(logger);
        var state = SeedSimulation();

        var teacherItem = new Provenance(
            Kind: ProvenanceKind.TeacherAuthoredOriginal,
            Recorded: DateTimeOffset.UtcNow,
            Source: "teacher/t-112");

        ExamSimulationDelivery.AssertDeliverable(
            gate: gate,
            state: state,
            itemId: "item-teacher-abc",
            provenance: teacherItem,
            tenantId: "tenant-cena-demo",
            actorId: "StudentActor/stu-007");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Deliverable_Phantom_Type_Refuses_Ministry_Provenance_At_Compile_Time_Seam()
    {
        var ministry = new Provenance(
            ProvenanceKind.MinistryBagrut,
            DateTimeOffset.UtcNow,
            "Ministry code 805");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Deliverable<string>.From("<question body redacted>", ministry));

        Assert.Contains("reference-only", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deliverable_Phantom_Type_Accepts_AiRecreated_Provenance()
    {
        var recreated = new Provenance(
            ProvenanceKind.AiRecreated,
            DateTimeOffset.UtcNow,
            "BagrutRecreation/rec-1");

        var wrapped = Deliverable<string>.From("q: what is 2+2?", recreated);

        Assert.Equal("q: what is 2+2?", wrapped.Value);
        Assert.Equal(ProvenanceKind.AiRecreated, wrapped.Provenance.Kind);
        Assert.True(wrapped.Provenance.IsDeliverable);
    }

    // ---- test helpers ----

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);
}
