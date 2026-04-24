// =============================================================================
// Cena Platform — Fake Isomorph Generator (prr-201 tests)
//
// In-memory IIsomorphGenerator driver. Tests configure the sequence of
// responses and the fake returns them in order, counting invocations so
// tests can assert "stage 2 was never called" or "stage 2 was called N
// times".
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

internal sealed class FakeIsomorphGenerator : IIsomorphGenerator
{
    private readonly Queue<IsomorphResult> _responses = new();
    public int CallCount { get; private set; }
    public List<IsomorphRequest> Requests { get; } = new();

    public FakeIsomorphGenerator Enqueue(IsomorphResult result)
    {
        _responses.Enqueue(result);
        return this;
    }

    public FakeIsomorphGenerator EnqueueCandidates(params IsomorphCandidate[] candidates)
    {
        _responses.Enqueue(new IsomorphResult(
            IsomorphVerdict.Ok, candidates, 0.015 * candidates.Length, null));
        return this;
    }

    public FakeIsomorphGenerator EnqueueCircuitOpen()
    {
        _responses.Enqueue(new IsomorphResult(
            IsomorphVerdict.CircuitOpen, Array.Empty<IsomorphCandidate>(), 0, "circuit open"));
        return this;
    }

    public Task<IsomorphResult> GenerateAsync(IsomorphRequest request, CancellationToken ct = default)
    {
        CallCount++;
        Requests.Add(request);
        if (_responses.Count == 0)
        {
            return Task.FromResult(new IsomorphResult(
                IsomorphVerdict.Ok, Array.Empty<IsomorphCandidate>(), 0, null));
        }
        return Task.FromResult(_responses.Dequeue());
    }
}
