// =============================================================================
// Cena Platform — Insufficient Slot Space Exception (prr-200)
//
// Per TASK-PRR-200 DoD: the compiler throws this when the template's slot
// constraint space cannot yield the requested count of distinct variants.
// No silent partial result.
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

public sealed class InsufficientSlotSpaceException : InvalidOperationException
{
    public string TemplateId { get; }
    public int Requested { get; }
    public int Produced { get; }
    public long SlotSpaceUpperBound { get; }

    public InsufficientSlotSpaceException(
        string templateId,
        int requested,
        int produced,
        long slotSpaceUpperBound,
        string? extra = null)
        : base(BuildMessage(templateId, requested, produced, slotSpaceUpperBound, extra))
    {
        TemplateId = templateId;
        Requested = requested;
        Produced = produced;
        SlotSpaceUpperBound = slotSpaceUpperBound;
    }

    private static string BuildMessage(string id, int req, int prod, long bound, string? extra) =>
        $"Template '{id}' could produce only {prod}/{req} distinct variants " +
        $"(slot-space upper bound ≈ {bound}). " +
        $"Widen the slot ranges, relax accept_shapes, or lower the requested count." +
        (extra is null ? "" : $" Detail: {extra}");
}
