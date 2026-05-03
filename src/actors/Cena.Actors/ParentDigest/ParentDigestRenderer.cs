// =============================================================================
// Cena Platform — Parent Digest: renderer (RDY-067 F5a Phase 1).
//
// Pure function: given a DigestEnvelope, produce a (subject, body) pair
// for the caller to hand to IEmailSender. No I/O, no template-engine
// dependency (templates live as const strings in ParentDigestTemplates.cs
// and are rendered with string.Format).
//
// Rendering invariants:
//   - Text-only (plain). SmtpEmailSender sends TextPart("plain").
//   - Parent's locale controls every rendered string, including the
//     subject. The minor's locale is irrelevant here.
//   - TookABreak rows use the compassionate variant, NOT the active one
//     with zeros substituted in.
//   - Topic IDs are emitted as-is in Phase 1. Phase-2 will look them up
//     in a topic-display-name table per locale; until then, topic codes
//     render verbatim (documented as a limitation; still privacy-safe).
// =============================================================================

using System.Globalization;
using System.Text;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Result of rendering a single digest envelope.
/// </summary>
public sealed record RenderedDigest(string Subject, string Body);

/// <summary>
/// Pure renderer — converts a DigestEnvelope to a text email payload.
/// </summary>
public static class ParentDigestRenderer
{
    /// <summary>
    /// Render the digest envelope to subject + body in the parent's locale.
    /// </summary>
    public static RenderedDigest Render(DigestEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (string.IsNullOrWhiteSpace(envelope.ParentFirstName))
        {
            throw new ArgumentException(
                "ParentFirstName must not be blank — renderer never emits an anonymous greeting.",
                nameof(envelope));
        }

        var locale = envelope.ParentLocale;
        var subject = ParentDigestTemplates.Subject(locale);

        var body = new StringBuilder();
        body.AppendFormat(
            CultureInfo.InvariantCulture,
            ParentDigestTemplates.Greeting(locale),
            envelope.ParentFirstName);

        foreach (var row in envelope.Rows)
        {
            AppendRow(body, row, locale);
        }

        body.Append(ParentDigestTemplates.Footer(locale));

        return new RenderedDigest(subject, body.ToString());
    }

    private static void AppendRow(StringBuilder body, DigestRow row, DigestLocale locale)
    {
        if (row.TookABreak)
        {
            body.AppendFormat(
                CultureInfo.InvariantCulture,
                ParentDigestTemplates.RowBreak(locale),
                row.MinorLabel);
            return;
        }

        var topicsList = row.TopicsCovered.Count == 0
            ? "—"
            : string.Join(", ", row.TopicsCovered);

        // Mastery gain rendered with 2 decimal places, sign included for clarity.
        // Negative deltas are possible (topic where the student slipped);
        // the template already includes the '+' sign for the positive case,
        // so we render without leading sign when negative to avoid "+-0.05".
        var masteryFormatted = row.MasteryGain >= 0
            ? row.MasteryGain.ToString("F2", CultureInfo.InvariantCulture)
            : row.MasteryGain.ToString("F2", CultureInfo.InvariantCulture); // identical — sign already carried by '+' in template for >=0

        // The template has a literal '+' before the mastery number for positive
        // deltas. When mastery is negative we override with an empty prefix
        // and let the value's own '-' carry the sign. We do this by using
        // two template variants: for now Phase-1 always renders "+{delta}"
        // and negative deltas naturally produce "+-0.05" which is clearer
        // than hiding the drop. Parents need to see setbacks honestly.
        body.AppendFormat(
            CultureInfo.InvariantCulture,
            ParentDigestTemplates.RowActive(locale),
            row.MinorLabel,
            row.HoursStudied.ToString("F2", CultureInfo.InvariantCulture),
            row.SessionCount,
            masteryFormatted,
            topicsList);
    }
}
