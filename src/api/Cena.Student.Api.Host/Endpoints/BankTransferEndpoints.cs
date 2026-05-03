// =============================================================================
// Cena Platform — Bank-transfer checkout endpoints (parent) (EPIC-PRR-I PRR-304)
//
// Authenticated parent-scoped endpoints under /api/me/subscription/bank-transfer:
//   POST /reserve      — create a Pending reservation, return code + bank details
//   GET  /{referenceCode} — status check (Pending | Confirmed | Expired)
//
// Annual-prepay only per PRR-304 scope; the request does not carry a cycle
// field for that reason. The admin-side confirm endpoint lives on the
// Admin API host, not here.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>
/// Configuration for the payee bank details rendered back to parents on
/// reservation. Bind <c>BankTransfer:PayeeDetails:*</c>. Required at boot
/// for the endpoint to respond 200 — a misconfigured host returns 503 so
/// no parent is ever shown a half-usable reference code.
/// </summary>
public sealed class BankTransferPayeeOptions
{
    public const string SectionName = "BankTransfer:PayeeDetails";

    public string BankName { get; set; } = "";
    public string BranchCode { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountHolder { get; set; } = "";
    public string? Notes { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BankName) &&
        !string.IsNullOrWhiteSpace(BranchCode) &&
        !string.IsNullOrWhiteSpace(AccountNumber) &&
        !string.IsNullOrWhiteSpace(AccountHolder);
}

/// <summary>Bank-transfer parent endpoints under /api/me/subscription/bank-transfer.</summary>
public static class BankTransferEndpoints
{
    /// <summary>Register the parent-side bank-transfer endpoint group.</summary>
    public static IEndpointRouteBuilder MapBankTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/subscription/bank-transfer")
            .WithTags("Subscriptions")
            .RequireAuthorization();

        group.MapPost("reserve", Reserve).WithName("ReserveBankTransfer");
        group.MapGet("{referenceCode}", GetStatus).WithName("GetBankTransferStatus");

        return app;
    }

    // ----- POST reserve -----

    private static async Task<IResult> Reserve(
        HttpContext http,
        [FromBody] BankTransferReserveRequest body,
        [FromServices] BankTransferReservationService service,
        [FromServices] IOptions<BankTransferPayeeOptions> payeeOptions,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (string.IsNullOrWhiteSpace(body?.PrimaryStudentId))
        {
            return Results.BadRequest(new { error = "primary_student_required" });
        }
        if (!Enum.TryParse<SubscriptionTier>(body.Tier, ignoreCase: true, out var tier))
        {
            return Results.BadRequest(new { error = "invalid_tier" });
        }

        var payee = payeeOptions.Value;
        if (!payee.IsConfigured)
        {
            // Fail-loud per memory "No stubs — production grade": a host
            // without payee details cannot honestly render a reference
            // code, so return 503 rather than silently omitting fields.
            return Results.Problem(
                title: "bank_transfer_misconfigured",
                detail: "BankTransfer:PayeeDetails is not configured on this host.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        BankTransferReservationDocument doc;
        try
        {
            doc = await service.ReserveAsync(
                parentSubjectIdEncrypted: parentId,
                primaryStudentSubjectIdEncrypted: body.PrimaryStudentId,
                tier: tier,
                ct: ct);
        }
        catch (BankTransferReservationException ex)
        {
            return Results.BadRequest(new { error = ex.ReasonCode, details = ex.Message });
        }

        var payeeDto = new BankTransferPayeeDetailsDto(
            BankName: payee.BankName,
            BranchCode: payee.BranchCode,
            AccountNumber: payee.AccountNumber,
            AccountHolder: payee.AccountHolder,
            Notes: payee.Notes);

        return Results.Ok(new BankTransferReserveResponse(
            ReferenceCode: doc.ReferenceCode,
            AmountAgorot: doc.AmountAgorot,
            Currency: "ILS",
            Tier: doc.Tier.ToString(),
            ExpiresAt: doc.ExpiresAt,
            PayeeDetails: payeeDto));
    }

    // ----- GET {referenceCode} -----

    private static async Task<IResult> GetStatus(
        HttpContext http,
        string referenceCode,
        [FromServices] BankTransferReservationService service,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        var doc = await service.GetAsync(referenceCode, ct);
        if (doc is null)
        {
            return Results.NotFound(new { error = "reservation_not_found" });
        }
        // Tenant-scope: a parent can only see their own reservation.
        // Admin-side listing lives on the Admin API host.
        if (doc.ParentSubjectIdEncrypted != parentId)
        {
            return Results.NotFound(new { error = "reservation_not_found" });
        }

        return Results.Ok(new BankTransferStatusResponse(
            ReferenceCode: doc.ReferenceCode,
            Status: doc.Status.ToString(),
            AmountAgorot: doc.AmountAgorot,
            Tier: doc.Tier.ToString(),
            CreatedAt: doc.CreatedAt,
            ExpiresAt: doc.ExpiresAt,
            ConfirmedAt: doc.ConfirmedAt,
            ExpiredAt: doc.ExpiredAt));
    }

    private static string RequireParentId(HttpContext http)
    {
        var id = http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new UnauthorizedAccessException("sub claim missing.");
        }
        return id;
    }
}
