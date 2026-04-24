// =============================================================================
// Cena Platform — NoopPhotoRedactor tests (EPIC-PRR-J PRR-413)
//
// Verifies the null-object fixture honestly reports deferred work and does
// not mutate input bytes. The honesty contract is load-bearing: if the Noop
// redactor gets bound to production by accident, the "redaction_not_configured"
// tag in the audit-log will make the misconfiguration visible at the next
// dashboard refresh, not at breach-notification time.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic.Intake;

public class NoopPhotoRedactorTests
{
    private static PhotoRedactionRequest Request(
        PhotoRedactionKinds kinds = PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.BottomMargin,
        string mimeType = "image/jpeg",
        int width = 1024,
        int height = 1536) =>
        new(mimeType, width, height, kinds);

    [Fact]
    public async Task ReturnsInputBytesUnchanged()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var redactor = new NoopPhotoRedactor();

        var result = await redactor.RedactAsync(bytes, Request(), CancellationToken.None);

        // Same memory handle returned — no copy, no mutation.
        Assert.Equal(bytes, result.RedactedImageBytes.ToArray());
    }

    [Fact]
    public async Task AppliedRedactionsIsEmpty()
    {
        var redactor = new NoopPhotoRedactor();

        var result = await redactor.RedactAsync(
            ReadOnlyMemory<byte>.Empty,
            Request(PhotoRedactionKinds.TopMargin | PhotoRedactionKinds.Faces),
            CancellationToken.None);

        Assert.Empty(result.AppliedRedactions);
    }

    [Fact]
    public async Task DeferredContainsRedactionNotConfiguredTag()
    {
        var redactor = new NoopPhotoRedactor();

        var result = await redactor.RedactAsync(
            ReadOnlyMemory<byte>.Empty,
            Request(PhotoRedactionKinds.None),
            CancellationToken.None);

        Assert.Contains(NoopPhotoRedactor.RedactionNotConfiguredTag, result.DeferredRedactions);
    }

    [Fact]
    public async Task DeferredEnumeratesEveryRequestedKind()
    {
        var redactor = new NoopPhotoRedactor();
        var all = PhotoRedactionKinds.TopMargin
                  | PhotoRedactionKinds.BottomMargin
                  | PhotoRedactionKinds.Faces
                  | PhotoRedactionKinds.Logos;

        var result = await redactor.RedactAsync(
            ReadOnlyMemory<byte>.Empty,
            Request(all),
            CancellationToken.None);

        Assert.Contains("top_margin_not_implemented", result.DeferredRedactions);
        Assert.Contains("bottom_margin_not_implemented", result.DeferredRedactions);
        Assert.Contains("face_detection_not_implemented", result.DeferredRedactions);
        Assert.Contains("logo_template_matcher_not_implemented", result.DeferredRedactions);
    }

    [Fact]
    public async Task NullRequestThrows()
    {
        var redactor = new NoopPhotoRedactor();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            redactor.RedactAsync(ReadOnlyMemory<byte>.Empty, null!, CancellationToken.None));
    }
}
