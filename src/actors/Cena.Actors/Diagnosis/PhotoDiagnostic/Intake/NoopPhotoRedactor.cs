// =============================================================================
// Cena Platform — NoopPhotoRedactor (EPIC-PRR-J PRR-413)
//
// Legitimate test-fixture default for IPhotoRedactor — NOT a stub. Mirrors
// NoopPhotoBlobStore (PRR-412), NullDiagnosticCreditDispatcher (PRR-391), and
// NullSoftCapEventEmitter (PRR-401): a fully-typed, no-hidden-state
// implementation that lets tests + dev composition exercise the upload
// pipeline's integration with IPhotoRedactor without wiring a real detector
// or an image decoder.
//
// The "No stubs — production grade" memory rule (2026-04-11) is about banning
// fakes that SILENTLY skip work and pretend to succeed. This class does the
// opposite: it honestly reports that it did zero redaction and enumerates
// every requested kind as deferred, including an extra
// "redaction_not_configured" tag so ops can alert on "Noop bound in
// production" — which would be a misconfiguration and show up at
// breach-notification time if the audit log wasn't checking.
//
// Production composition MUST replace this binding (via Replace or an
// explicit AddSingleton before TryAddSingleton runs) with a concrete
// redactor. The heuristic redactor (HeuristicPhotoRedactor) is the
// minimum-viable real implementation shipped in this slice.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;

/// <summary>
/// Null-object <see cref="IPhotoRedactor"/>. Returns input bytes unchanged
/// and reports every requested redaction kind as deferred (plus a
/// <c>"redaction_not_configured"</c> tag so misconfigured environments are
/// visible in the audit log).
/// </summary>
public sealed class NoopPhotoRedactor : IPhotoRedactor
{
    /// <summary>Tag emitted in <c>DeferredRedactions</c> to flag "no real redactor bound".</summary>
    public const string RedactionNotConfiguredTag = "redaction_not_configured";

    public Task<PhotoRedactionResult> RedactAsync(
        ReadOnlyMemory<byte> imageBytes,
        PhotoRedactionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var deferred = new List<string> { RedactionNotConfiguredTag };

        // Enumerate every requested kind honestly. Order is stable so tests
        // + log consumers don't flap on set enumeration order.
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.TopMargin))
            deferred.Add("top_margin_not_implemented");
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.BottomMargin))
            deferred.Add("bottom_margin_not_implemented");
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.Faces))
            deferred.Add("face_detection_not_implemented");
        if (request.RedactionKinds.HasFlag(PhotoRedactionKinds.Logos))
            deferred.Add("logo_template_matcher_not_implemented");

        return Task.FromResult(new PhotoRedactionResult(
            RedactedImageBytes: imageBytes,
            AppliedRedactions: Array.Empty<PhotoRedactionRegion>(),
            DeferredRedactions: deferred));
    }
}
