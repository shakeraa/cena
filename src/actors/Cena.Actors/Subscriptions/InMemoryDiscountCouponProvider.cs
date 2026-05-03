// =============================================================================
// Cena Platform — InMemoryDiscountCouponProvider (per-user discount-codes feature)
//
// Deterministic in-memory adapter. Used in:
//   - Unit tests where reaching out to the Stripe sandbox is undesirable
//   - Dev-host composition (no Stripe key configured)
//   - The admin-API host (default registration), since admin doesn't
//     necessarily have Stripe credentials wired the way Student API does
//
// The IDs returned are deterministic per AssignmentId so two consecutive
// calls for the same assignment produce identical CouponId / PromotionCodeId
// values — convenient for tests asserting roundtrip behaviour.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Production-grade in-memory <see cref="IDiscountCouponProvider"/>. Tracks
/// created coupons so the test harness can introspect issuance + revocation
/// without a network round-trip.
/// </summary>
public sealed class InMemoryDiscountCouponProvider : IDiscountCouponProvider
{
    private readonly ConcurrentDictionary<string, CouponCreateRequest> _createdByCouponId = new();
    private readonly ConcurrentDictionary<string, bool> _revokedCouponIds = new();

    /// <inheritdoc/>
    public string Name => "in-memory";

    /// <summary>Coupon → original create request snapshot (test introspection).</summary>
    public IReadOnlyDictionary<string, CouponCreateRequest> Created => _createdByCouponId;

    /// <summary>Set of coupon ids whose revoke has been called (test introspection).</summary>
    public IReadOnlyCollection<string> Revoked => _revokedCouponIds.Keys.ToArray();

    /// <inheritdoc/>
    public Task<CouponCreateResult> CreateCouponAsync(
        CouponCreateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AssignmentId))
        {
            throw new ArgumentException(
                "AssignmentId is required.", nameof(request));
        }

        // Deterministic ids (test-friendly).
        var couponId = $"cou_inmem_{request.AssignmentId}";
        var promotionCodeId = $"promo_inmem_{request.AssignmentId}";
        // Customer-facing string in upper-case so it visually mirrors the
        // Stripe convention (Stripe auto-generated promo codes are upper-case).
        var promotionCodeString = $"CENA-{request.AssignmentId.ToUpperInvariant()}";

        _createdByCouponId[couponId] = request;
        return Task.FromResult(new CouponCreateResult(
            CouponId: couponId,
            PromotionCodeId: promotionCodeId,
            PromotionCodeString: promotionCodeString));
    }

    /// <inheritdoc/>
    public Task RevokeCouponAsync(CouponRevokeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.CouponId))
        {
            // Idempotent: nothing to revoke if no coupon id was passed.
            return Task.CompletedTask;
        }
        // Idempotent — calling twice is fine.
        _revokedCouponIds[request.CouponId] = true;
        return Task.CompletedTask;
    }
}
