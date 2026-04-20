// =============================================================================
// Cena Platform — SMS encoding helpers (prr-018).
//
// A single outbound SMS can carry one of two encodings depending on whether the
// body fits in the GSM 03.38 7-bit alphabet or requires UCS-2 (16-bit). The
// wire-level length cap differs:
//
//   GSM-7 single SMS segment : 160 characters
//   UCS-2 single SMS segment : 70 characters
//
// Anything longer is concatenated into multiple segments by the carrier, which
// costs more per-message and introduces ordering risk. For parent-nudge traffic
// Cena deliberately stays inside a single segment — the Policy chain rejects
// any text that would overflow rather than silently letting the vendor split.
//
// WHY: we refuse to split segments client-side.
//   1. Cost: two segments = 2x finops, and the finops lens (persona-finops)
//      called this out in the pre-release review.
//   2. Ordering: multi-segment SMS can arrive out of order on some carriers —
//      the parent sees "…break" before "Reminder: Noa took a" which reads as
//      fragments of something alarming.
//   3. Ship-gate clarity: a forced 160/70 ceiling forces authors to write
//      terse, honest copy instead of burying the ask in paragraphs.
//
// WHY: we normalise control characters aggressively.
//   RTL-overrides (U+202E) and bidirectional embeddings are a classic SMS
//   phishing vector — a message like "Your tutor sent you a link: " +
//   RLO + "moc.livee/" can display as "Your tutor sent you a link: evil.com/".
//   Stripping those (and all C0/C1 control chars) is cheap and closes the
//   surface. We do NOT strip emoji — parents in the persona panel (Rami/Rachel)
//   valued human warmth over purity.
//
// Reference: 3GPP TS 23.038 §6.2.1 (GSM 7-bit default alphabet + extensions).
// =============================================================================

using System.Globalization;
using System.Text;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Wire encoding a sanitised SMS body will travel in. The vendor adapter
/// (Twilio) negotiates this automatically, but we compute it ourselves so the
/// length cap test is deterministic.
/// </summary>
public enum SmsEncoding
{
    /// <summary>3GPP TS 23.038 7-bit default + extension tables. 160-char segment.</summary>
    Gsm7 = 0,

    /// <summary>UTF-16 code units (UCS-2 / BMP). 70-char segment.</summary>
    Ucs2 = 1,
}

/// <summary>
/// Stateless helpers for classifying and measuring SMS bodies. Pure functions
/// — no I/O, no allocation beyond StringBuilder, safe to call from hot paths.
/// </summary>
public static class SmsEncodingRules
{
    /// <summary>Max characters per single segment, GSM-7.</summary>
    public const int Gsm7SingleSegmentMax = 160;

    /// <summary>Max UTF-16 code units per single segment, UCS-2.</summary>
    public const int Ucs2SingleSegmentMax = 70;

    /// <summary>
    /// The GSM 03.38 basic 7-bit alphabet. Characters outside this table (and
    /// the extension table below) force UCS-2 encoding.
    /// </summary>
    private static readonly HashSet<char> Gsm7Basic = new(
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ ÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà");

    /// <summary>
    /// GSM 03.38 extension table — each char here takes TWO 7-bit septets, so
    /// we count it as 2 when measuring length. The escape character (0x1B)
    /// itself is never transmitted as content, only as a prefix.
    /// </summary>
    private static readonly HashSet<char> Gsm7Extension = new(
        "^{}\\[~]|€");

    /// <summary>
    /// Classify a raw body string into GSM-7 vs UCS-2. Returns GSM-7 iff every
    /// character fits the basic or extension table (after the sanitiser has
    /// already removed control chars).
    /// </summary>
    public static SmsEncoding Classify(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var ch in body)
        {
            if (Gsm7Basic.Contains(ch)) continue;
            if (Gsm7Extension.Contains(ch)) continue;
            return SmsEncoding.Ucs2;
        }
        return SmsEncoding.Gsm7;
    }

    /// <summary>
    /// Measure the on-the-wire length of a body given its encoding. For GSM-7
    /// each basic char counts 1 and each extension char counts 2. For UCS-2
    /// each UTF-16 code unit counts 1 (BMP chars = 1, non-BMP surrogate pairs
    /// = 2). This matches what the carrier will bill for.
    /// </summary>
    public static int MeasuredLength(string body, SmsEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (encoding == SmsEncoding.Gsm7)
        {
            var len = 0;
            foreach (var ch in body)
            {
                if (Gsm7Extension.Contains(ch)) len += 2;
                else len += 1;
            }
            return len;
        }

        // UCS-2: UTF-16 code units. String.Length is exactly that in .NET.
        return body.Length;
    }

    /// <summary>
    /// Single-segment cap for the given encoding. Callers that want to enforce
    /// the "no carrier-side concatenation" rule compare
    /// <see cref="MeasuredLength"/> to this value.
    /// </summary>
    public static int SingleSegmentCap(SmsEncoding encoding) => encoding switch
    {
        SmsEncoding.Gsm7 => Gsm7SingleSegmentMax,
        SmsEncoding.Ucs2 => Ucs2SingleSegmentMax,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>
    /// Strip characters that have no legitimate place in a parent-nudge SMS:
    ///   - ASCII C0 control chars except LF (\n) — we drop CR, HT, BS, NUL etc.
    ///     LF is retained because some providers map it to a visual line break.
    ///   - ASCII C1 control chars (U+0080 – U+009F) — entirely dropped.
    ///   - Unicode bidi overrides and embeddings (U+202A – U+202E, U+2066 – U+2069)
    ///     used in RLO/LRO phishing attacks.
    ///   - Unicode format chars that can hide content (U+200B – U+200F,
    ///     U+2028, U+2029, U+FEFF).
    /// Other characters — including emoji (surrogate pairs) — are preserved.
    ///
    /// WHY retain LF: multi-line SMS is legitimate for vendor templates
    /// ("Rami,\nNoa studied 3h this week..."). WHY drop CR and TAB: they can
    /// produce ambiguous UI (some clients treat CR as a segment break).
    /// </summary>
    public static string StripControlAndBidi(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length == 0) return raw;

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch == '\n') { sb.Append(ch); continue; }
            if (ch < 0x20) continue;                 // C0 except LF
            if (ch >= 0x7F && ch <= 0x9F) continue;  // DEL + C1
            if (IsBidiOrFormat(ch)) continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool IsBidiOrFormat(char ch)
    {
        // LRE/RLE/PDF/LRO/RLO
        if (ch >= 0x202A && ch <= 0x202E) return true;
        // LRI/RLI/FSI/PDI
        if (ch >= 0x2066 && ch <= 0x2069) return true;
        // ZWSP/ZWNJ/ZWJ/LRM/RLM
        if (ch >= 0x200B && ch <= 0x200F) return true;
        // Line/paragraph separators + BOM
        if (ch == 0x2028 || ch == 0x2029 || ch == 0xFEFF) return true;

        // Catch any other Cf (Format) category char with the BCL rather than
        // hand-maintaining a table. Cf is narrow enough that false positives
        // are rare (soft hyphen, Arabic number sign) — those are safe to drop
        // from SMS where layout is the carrier's job.
        var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
        return cat == UnicodeCategory.Format;
    }

    /// <summary>
    /// Normalise whitespace: collapse runs of horizontal spaces to one, trim
    /// leading/trailing whitespace. Does NOT touch newlines.
    /// </summary>
    public static string NormalizeWhitespace(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var sb = new StringBuilder(input.Length);
        var inSpace = false;
        foreach (var ch in input)
        {
            if (ch == ' ' || ch == '\t')
            {
                if (!inSpace) { sb.Append(' '); inSpace = true; }
            }
            else
            {
                sb.Append(ch);
                inSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}
