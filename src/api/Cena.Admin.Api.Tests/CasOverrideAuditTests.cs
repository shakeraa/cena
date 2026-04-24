// =============================================================================
// Cena Platform — CAS Override Endpoint Audit-Rule Tests (RDY-036 §14 / RDY-037)
//
// The CAS override endpoint is the ONLY way to release a question from a
// Failed / Unverifiable binding. Its audit rules are the bulwark against
// silently laundering bad answers past ADR-0002. These tests pin:
//   1. The env-flag gate (CENA_CAS_OVERRIDE_ENABLED must equal "true").
//   2. Reason length floor (20 chars, whitespace-trimmed).
//   3. Ticket required.
//   4. Status-code and error-code shape (caller depends on CenaError codes).
// =============================================================================

using Cena.Admin.Api.Endpoints;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Cena.Admin.Api.Tests;

public class CasOverrideAuditTests
{
    private static CasOverrideRequest ValidRequest() =>
        new(Reason: "Engine wrong on piecewise domain split — manual review passes.",
            Ticket: "JIRA-12345");

    // ── Env-flag gate ───────────────────────────────────────────────────

    [Fact]
    public void EnvDisabled_Returns403_WithDisabledErrorCode()
    {
        var result = CasOverrideEndpoint.ValidateOverrideRequest("q-1", ValidRequest(), envEnabled: false);

        Assert.NotNull(result);
        AssertHasErrorCode(result!, "CAS_OVERRIDE_DISABLED", expectedStatus: StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void EnvEnabled_ValidRequest_ReturnsNull_AllowingHandlerToProceed()
    {
        var result = CasOverrideEndpoint.ValidateOverrideRequest("q-1", ValidRequest(), envEnabled: true);
        Assert.Null(result);
    }

    // ── Question id validation ──────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void InvalidQuestionId_Returns400_WithTypedCode(string? id)
    {
        var result = CasOverrideEndpoint.ValidateOverrideRequest(id!, ValidRequest(), envEnabled: true);
        Assert.NotNull(result);
        AssertHasErrorCode(result!, "INVALID_QUESTION_ID", expectedStatus: StatusCodes.Status400BadRequest);
    }

    // ── Reason length floor ─────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("too short")]                 // 9 chars
    [InlineData("nineteen characters")]       // 19 chars — boundary
    [InlineData("                      ")]    // whitespace-only, long
    public void ShortOrBlankReason_Returns400(string? reason)
    {
        var result = CasOverrideEndpoint.ValidateOverrideRequest(
            "q-1",
            new CasOverrideRequest(reason!, "JIRA-1"),
            envEnabled: true);

        Assert.NotNull(result);
        AssertHasErrorCode(result!, "INVALID_OVERRIDE_REASON", expectedStatus: StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ExactlyMinReasonLength_Passes()
    {
        var reason = new string('x', CasOverrideEndpoint.MinReasonLength);
        var result = CasOverrideEndpoint.ValidateOverrideRequest(
            "q-1",
            new CasOverrideRequest(reason, "JIRA-1"),
            envEnabled: true);

        Assert.Null(result); // Passes validation
    }

    [Fact]
    public void ReasonLengthIsMeasured_AfterTrim()
    {
        // A long-looking reason that collapses below the floor after trim
        // must still fail.
        var reason = "  short  "; // 5 chars after trim
        var result = CasOverrideEndpoint.ValidateOverrideRequest(
            "q-1",
            new CasOverrideRequest(reason, "JIRA-1"),
            envEnabled: true);

        Assert.NotNull(result);
        AssertHasErrorCode(result!, "INVALID_OVERRIDE_REASON", expectedStatus: StatusCodes.Status400BadRequest);
    }

    // ── Ticket requirement ──────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void MissingTicket_Returns400(string? ticket)
    {
        var result = CasOverrideEndpoint.ValidateOverrideRequest(
            "q-1",
            new CasOverrideRequest("A valid reason that exceeds the minimum length requirement.", ticket!),
            envEnabled: true);

        Assert.NotNull(result);
        AssertHasErrorCode(result!, "INVALID_OVERRIDE_TICKET", expectedStatus: StatusCodes.Status400BadRequest);
    }

    // ── Audit constants ─────────────────────────────────────────────────

    [Fact]
    public void AuditConstants_AreStableAndDocumented()
    {
        // Pinned: deployment scripts and ops runbooks depend on these exact
        // values. Changing them is a breaking change to the operator surface
        // and requires an ADR update.
        Assert.Equal("CENA_CAS_OVERRIDE_ENABLED", CasOverrideEndpoint.EnvFlag);
        Assert.Equal(20, CasOverrideEndpoint.MinReasonLength);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static void AssertHasErrorCode(IResult result, string expectedCode, int expectedStatus)
    {
        // Results.Json returns JsonHttpResult<T>; Results.BadRequest returns
        // BadRequest<T>. Both implement IStatusCodeHttpResult and IValueHttpResult.
        if (result is IStatusCodeHttpResult s)
        {
            Assert.Equal(expectedStatus, s.StatusCode);
        }
        else
        {
            Assert.Fail($"Result type {result.GetType().Name} doesn't expose StatusCode.");
        }

        if (result is IValueHttpResult v && v.Value is CenaError err)
        {
            Assert.Equal(expectedCode, err.Code);
        }
        else
        {
            Assert.Fail($"Result type {result.GetType().Name} doesn't carry CenaError value (got {result.GetType().Name}).");
        }
    }
}
