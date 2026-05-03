// =============================================================================
// Cena Platform — Layer 5 CAS Validation (ADR-0033, ADR-0002)
//
// The final gate. Every math block the cascade emits must round-trip through
// an ILatexValidator (wrapping CasRouterService in production). Blocks that
// fail parsing stay in the output but are flagged SympyParsed=false so the
// consumer (admin review queue / student step-solver) can reject them.
//
// This layer is intentionally unconditional — it never short-circuits on
// confidence. A high-confidence OCR that produces syntactically valid but
// semantically bogus LaTeX ("3x + 5 = 14 + qq") must still fail here.
//
// ADR-0002 invariant: nothing that fails CAS round-trip ever surfaces to
// students. That invariant lives at the consumer side (step-solver filters
// SympyParsed=true); this layer just produces the tag correctly.
// =============================================================================

using System.Diagnostics;
using Cena.Infrastructure.Ocr.Cas;
using Cena.Infrastructure.Ocr.Contracts;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer5CasValidation : ILayer5CasValidation
{
    private readonly ILatexValidator _validator;
    private readonly ILogger<Layer5CasValidation>? _log;

    public Layer5CasValidation(
        ILatexValidator validator,
        ILogger<Layer5CasValidation>? log = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _log = log;
    }

    public async Task<Layer5Output> RunAsync(
        IReadOnlyList<OcrMathBlock> mathBlocks,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (mathBlocks.Count == 0)
        {
            sw.Stop();
            return new Layer5Output(
                MathBlocks: Array.Empty<OcrMathBlock>(),
                Validated: 0,
                Failed: 0,
                LatencySeconds: sw.Elapsed.TotalSeconds);
        }

        var updated = new List<OcrMathBlock>(mathBlocks.Count);
        int validated = 0;
        int failed = 0;

        foreach (var block in mathBlocks)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(block.Latex))
            {
                updated.Add(block with { SympyParsed = false, CanonicalForm = null });
                failed++;
                continue;
            }

            LatexValidationResult result;
            try
            {
                result = await _validator.ValidateAsync(block.Latex, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Validator crashed on one block — reject it, don't abort the
                // whole cascade run.
                _log?.LogWarning(ex,
                    "[OCR_CASCADE] Layer5 validator threw on a block; marking CAS-failed");
                updated.Add(block with { SympyParsed = false, CanonicalForm = null });
                failed++;
                continue;
            }

            if (result.Parsed)
            {
                updated.Add(block with
                {
                    SympyParsed = true,
                    CanonicalForm = result.CanonicalForm,
                });
                validated++;
            }
            else
            {
                updated.Add(block with
                {
                    SympyParsed = false,
                    CanonicalForm = null,
                });
                failed++;
            }
        }

        sw.Stop();
        _log?.LogDebug(
            "[OCR_CASCADE] Layer5 cas_ok={Validated} cas_fail={Failed} latencyMs={Latency}",
            validated, failed, sw.Elapsed.TotalMilliseconds);

        return new Layer5Output(
            MathBlocks: updated,
            Validated: validated,
            Failed: failed,
            LatencySeconds: sw.Elapsed.TotalSeconds);
    }
}
