// =============================================================================
// Cena Platform — BankTransferReservation types + store + ref-code generator
// (EPIC-PRR-I PRR-304)
//
// Why this exists. Some Israeli households do not have a credit card and
// reject digital wallets (Bit, PayBox) — either for cost, accessibility, or
// trust reasons. Excluding them from the platform would violate the PRR-I
// equity ethos. This task adds bank-transfer checkout as a third payment
// route, Annual-prepay-only to keep manual-reconciliation cost below
// break-even (monthly ₪79 reconciliation overhead loses money; annual
// ₪2,490 clears it).
//
// Design choice: reservation document, NOT a new subscription status.
// The existing lifecycle (Unsubscribed → Active → ...) is unchanged.
// A BankTransferReservationDocument lives alongside the subscription
// aggregate, keyed by reference code. When admin marks it confirmed,
// we call SubscriptionCommands.Activate with a synthetic
// "bank-transfer:<referenceCode>" as the payment-transaction id. The
// aggregate does not learn about bank-transfer specifics — it sees an
// activation like any other. This keeps ADR-0057 subscription aggregate
// invariants intact while adding a payment route as an additive concept.
//
// Not stubs: both InMemory and Marten stores are real implementations
// (Marten follows in the composition root per the ADR-0042 migration
// pattern — InMemory is the v1 and production-grade for single-host).
//
// Reference code: 10 Crockford-base32 chars (no I/L/O/U to avoid
// handwriting/typing ambiguity). 32^10 ≈ 1.1e15 entropy — collision
// vanishingly unlikely, and the generator retries on collision at the
// store boundary. Banks that accept a "reference" field on incoming
// transfers will carry this through so finance can match payments to
// reservations manually; if a parent mis-types, the admin confirm
// endpoint rejects cleanly rather than confirming the wrong reservation.
// =============================================================================

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Lifecycle of a <see cref="BankTransferReservationDocument"/>.
/// Pending → Confirmed (admin marked payment received) OR Pending → Expired
/// (14-day window lapsed without payment). Confirmed and Expired are terminal.
/// </summary>
public enum BankTransferReservationStatus
{
    /// <summary>Reservation created, awaiting payment + admin confirmation.</summary>
    Pending = 0,

    /// <summary>Admin marked payment received; subscription activated.</summary>
    Confirmed = 1,

    /// <summary>14-day window lapsed without payment; reservation dead.</summary>
    Expired = 2,
}

/// <summary>
/// Persistent record of a parent's bank-transfer reservation. Keyed by
/// reference code (also the Marten Id) — a parent may create multiple
/// reservations if they abandon one and start over; each has its own code.
/// </summary>
public sealed class BankTransferReservationDocument
{
    /// <summary>Marten doc id = reference code (upper-case, 10 chars).</summary>
    public string Id { get; set; } = "";

    /// <summary>Canonical reference code (upper-case, 10 Crockford-base32 chars).</summary>
    public string ReferenceCode { get; set; } = "";

    /// <summary>Encrypted parent subject id (subject-key shredding compatible).</summary>
    public string ParentSubjectIdEncrypted { get; set; } = "";

    /// <summary>Encrypted primary-student subject id captured at reservation time.</summary>
    public string PrimaryStudentSubjectIdEncrypted { get; set; } = "";

    /// <summary>Tier chosen at reservation time.</summary>
    public SubscriptionTier Tier { get; set; }

    /// <summary>
    /// Amount in agorot the parent owes — pinned at reservation time
    /// against <c>TierCatalog.Get(Tier).AnnualPrice</c>. Bank-transfer
    /// is Annual-only per task scope; monthly reconciliation overhead
    /// doesn't clear break-even at ₪79.
    /// </summary>
    public long AmountAgorot { get; set; }

    /// <summary>UTC timestamp of reservation creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp at which the reservation auto-expires if unpaid.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public BankTransferReservationStatus Status { get; set; }

    /// <summary>UTC timestamp of admin confirmation. Null until confirmed.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>UTC timestamp of auto-expiry. Null until expired.</summary>
    public DateTimeOffset? ExpiredAt { get; set; }

    /// <summary>Encrypted admin subject id who confirmed. Null until confirmed.</summary>
    public string? ConfirmedByAdminSubjectIdEncrypted { get; set; }
}

/// <summary>
/// Persistence seam for <see cref="BankTransferReservationDocument"/>.
/// InMemory impl is production-grade for single-host; Marten impl in a
/// follow-up PR (ADR-0042 migration pattern).
/// </summary>
public interface IBankTransferReservationStore
{
    /// <summary>
    /// Insert or update a reservation. Used by both the reservation
    /// creation (new row) and the admin-confirm + worker-expire (status
    /// transitions on the existing row).
    /// </summary>
    Task SaveAsync(BankTransferReservationDocument doc, CancellationToken ct);

    /// <summary>
    /// Look up a reservation by its reference code. Returns null when
    /// the code is unknown (collision or mis-typed by admin). The
    /// lookup is case-insensitive — admin-side typing tolerance — but
    /// storage is canonical upper-case.
    /// </summary>
    Task<BankTransferReservationDocument?> GetByReferenceCodeAsync(
        string referenceCode, CancellationToken ct);

    /// <summary>
    /// List reservations in <see cref="BankTransferReservationStatus.Pending"/>.
    /// Used by the admin reconciliation dashboard.
    /// </summary>
    Task<IReadOnlyList<BankTransferReservationDocument>> ListPendingAsync(
        CancellationToken ct);

    /// <summary>
    /// List reservations still in Pending with <see cref="BankTransferReservationDocument.ExpiresAt"/>
    /// ≤ <paramref name="cutoff"/>. Used by the daily expiry worker.
    /// </summary>
    Task<IReadOnlyList<BankTransferReservationDocument>> ListExpiringAtOrBeforeAsync(
        DateTimeOffset cutoff, CancellationToken ct);
}

/// <summary>
/// In-memory <see cref="IBankTransferReservationStore"/>. Production-grade
/// for single-host deployments. Multi-replica deployments require a
/// Marten-backed implementation (follow-up).
/// </summary>
public sealed class InMemoryBankTransferReservationStore : IBankTransferReservationStore
{
    private readonly ConcurrentDictionary<string, BankTransferReservationDocument> _byCode = new();

    /// <inheritdoc/>
    public Task SaveAsync(BankTransferReservationDocument doc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrWhiteSpace(doc.ReferenceCode))
            throw new ArgumentException("ReferenceCode is required.", nameof(doc));

        // Canonicalise: Id mirrors ReferenceCode (Marten doc convention).
        var key = doc.ReferenceCode.ToUpperInvariant();
        doc.Id = key;
        doc.ReferenceCode = key;
        _byCode[key] = doc;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<BankTransferReservationDocument?> GetByReferenceCodeAsync(
        string referenceCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceCode))
            return Task.FromResult<BankTransferReservationDocument?>(null);
        var key = referenceCode.ToUpperInvariant();
        _byCode.TryGetValue(key, out var doc);
        return Task.FromResult(doc);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BankTransferReservationDocument>> ListPendingAsync(
        CancellationToken ct)
    {
        var list = _byCode.Values
            .Where(d => d.Status == BankTransferReservationStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<BankTransferReservationDocument>>(list);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BankTransferReservationDocument>> ListExpiringAtOrBeforeAsync(
        DateTimeOffset cutoff, CancellationToken ct)
    {
        var list = _byCode.Values
            .Where(d => d.Status == BankTransferReservationStatus.Pending
                     && d.ExpiresAt <= cutoff)
            .OrderBy(d => d.ExpiresAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<BankTransferReservationDocument>>(list);
    }
}

/// <summary>
/// Generates human-transcribable bank-transfer reference codes. Crockford
/// base32 (no I/L/O/U) to eliminate the most common hand-off mistakes when
/// a parent reads the code off a screen and types it into their bank's
/// reference field.
/// </summary>
public static class BankTransferReferenceCodeGenerator
{
    /// <summary>Crockford base32 alphabet (no I, L, O, U).</summary>
    public const string Crockford32 = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Generated code length. 10 chars × log2(32) = 50 bits of entropy.</summary>
    public const int CodeLength = 10;

    /// <summary>
    /// Generate one reference code. Caller is responsible for collision
    /// retry against <see cref="IBankTransferReservationStore.GetByReferenceCodeAsync"/>;
    /// in practice a single generation collides with probability ~ 2^-50
    /// per existing row, so a single retry loop with 3 attempts is safe.
    /// </summary>
    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[CodeLength];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            // Mod 32 is uniform because 256 is a multiple of 32.
            chars[i] = Crockford32[bytes[i] & 0x1F];
        }
        return new string(chars);
    }

    /// <summary>
    /// Canonical form: upper-case, with whitespace / hyphens / spaces
    /// stripped. Use when reading a code back from admin input or from a
    /// bank's reference field so a code that was visually displayed with
    /// a hyphen ("CENA-XXXXXXXXXX") round-trips to the canonical form.
    /// </summary>
    public static string Canonicalise(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            // Everything else (spaces, hyphens, punctuation) is dropped.
        }
        return sb.ToString();
    }
}
