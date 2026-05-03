// =============================================================================
// Cena Platform — Mock-exam item preview + delivery gate (extracted from MockExamRunService)
//
// Single-responsibility: serve a question preview to the runner UI while
// enforcing the ADR-0043 deliverable invariant on every served item, and
// emit ExamSimulationItemDelivered_V1 on the per-student stream so audit
// can answer "did we serve item X to student Y" by event-stream replay
// (Phase-4 #2).
//
// Extracted from MockExamRunService for the 500-LOC ratchet (ADR-0012).
// Behaviour preserved exactly; runner delegates GetQuestionPreviewAsync
// to MockExamItemPreview.GetAsync.
// =============================================================================

using Cena.Actors.Content;
using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Assessment;

public sealed class MockExamItemPreview
{
    private readonly IDocumentStore _store;
    private readonly IItemDeliveryGate _deliveryGate;
    private readonly TimeProvider _clock;

    public MockExamItemPreview(
        IDocumentStore store,
        IItemDeliveryGate deliveryGate,
        TimeProvider? clock = null)
    {
        _store = store;
        _deliveryGate = deliveryGate;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Serve a question preview for an active run. Runs the ADR-0043 gate
    /// on every served item + appends ExamSimulationItemDelivered_V1 on
    /// the student's stream so audit can replay the served-items list.
    /// </summary>
    public async Task<MockExamQuestionPreview?> GetAsync(
        string studentId, string runId, string questionId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var state = await session.LoadAsync<ExamSimulationState>(runId, ct);
        if (state is null || state.StudentId != studentId) return null;

        var validIds = state.PartAQuestionIds.Concat(state.PartBQuestionIds).ToHashSet();
        if (!validIds.Contains(questionId)) return null;

        // Multi-part Q's first; the runner needs subpart shape to render
        // multiple input rows.
        var multipart = await session.LoadAsync<BagrutMultipartQuestion>(questionId, ct);
        if (multipart is not null)
        {
            // ADR-0043 chokepoint — derive provenance from the doc and
            // throw on MinistryBagrut. Today TeacherAuthoredOriginal +
            // AiRecreated are the only legitimate writers; the gate is
            // belt-and-braces against a future writer slipping a Ministry
            // doc into the multi-part pool. Throw escapes to the endpoint
            // as 5xx (P0 SIEM alarm), which is correct.
            AssertDeliverable(multipart.SourceType, questionId, runId, studentId);

            await EmitItemDeliveredAsync(session, studentId, runId, questionId, multipart.SourceType, ct);

            return new MockExamQuestionPreview(
                QuestionId: questionId,
                Prompt: multipart.Stem,
                Topic: multipart.Topic,
                BloomsLevel: multipart.BloomsLevel,
                Subparts: multipart.Subparts
                    .Select(s => new MockExamSubpartPreview(s.PartId, s.Prompt, s.Points))
                    .ToList());
        }

        var rm = await session.LoadAsync<QuestionReadModel>(questionId, ct);
        var doc = await session.LoadAsync<QuestionDocument>(questionId, ct);
        if (doc is null) return null;

        // For single-cell Q's the derivation is "every active doc is
        // AiRecreated" by the same convention DiagnosticEndpoints uses
        // (see DeriveProvenanceKind). Belt-and-braces.
        var sourceType = rm?.SourceType ?? "AiRecreated";
        AssertDeliverable(sourceType, questionId, runId, studentId);
        await EmitItemDeliveredAsync(session, studentId, runId, questionId, sourceType, ct);

        return new MockExamQuestionPreview(
            QuestionId: questionId,
            Prompt: doc.Prompt,
            Topic: doc.Topic ?? rm?.Topic,
            BloomsLevel: rm?.BloomsLevel ?? 0,
            Subparts: null);
    }

    /// <summary>Phase-4 #2 — append an ExamSimulationItemDelivered_V1
    /// event so the per-student stream has an auditable record of every
    /// item served. Idempotency: this fires on every preview read, so
    /// the same item served twice = two events; the projection layer
    /// can dedup by (SimulationId, ItemId) when needed.</summary>
    private async Task EmitItemDeliveredAsync(
        IDocumentSession session, string studentId, string runId, string itemId,
        string sourceType, CancellationToken ct)
    {
        var kind = sourceType switch
        {
            "TeacherAuthoredOriginal" => ProvenanceKind.TeacherAuthoredOriginal,
            "MinistryBagrut"          => ProvenanceKind.MinistryBagrut, // unreachable — gate threw
            _                          => ProvenanceKind.AiRecreated,
        };
        session.Events.Append(studentId, new ExamSimulationItemDelivered_V1(
            StudentId: studentId,
            SimulationId: runId,
            ItemId: itemId,
            Provenance: kind,
            DeliveredAt: _clock.GetUtcNow()));
        await session.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps the doc's stored SourceType label to a Provenance + invokes
    /// the central gate. Throws on MinistryBagrut.
    /// </summary>
    private void AssertDeliverable(string sourceType, string itemId, string sessionId, string actorId)
    {
        var kind = sourceType switch
        {
            "TeacherAuthoredOriginal" => ProvenanceKind.TeacherAuthoredOriginal,
            "AiRecreated"             => ProvenanceKind.AiRecreated,
            "MinistryBagrut"          => ProvenanceKind.MinistryBagrut,
            _                          => ProvenanceKind.AiRecreated, // pragmatic default
        };
        // Best-effort tenantId — the runner state doesn't carry one
        // today (the run is keyed on studentId only). Gate logs both
        // sessionId + actorId, so trace+SIEM remain correlatable.
        _deliveryGate.AssertDeliverable(
            provenance: new Provenance(kind, _clock.GetUtcNow(), sourceType),
            itemId: itemId,
            sessionId: sessionId,
            tenantId: "(exam-prep-runner)",
            actorId: actorId);
    }
}
