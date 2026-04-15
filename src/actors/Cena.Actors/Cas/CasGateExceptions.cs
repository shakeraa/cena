// =============================================================================
// Cena Platform — CAS Gate Exceptions (RDY-034 / RDY-037, ADR-0002)
// Thrown by callers of the CAS ingestion gate when the gate rejects an
// operation in Enforce mode. Translated to 400/409 by endpoint handlers.
//
// RDY-037: relocated from Cena.Admin.Api.QualityGate → Cena.Actors.Cas so the
// contract lives in the domain layer alongside ICasRouterService.
// =============================================================================

namespace Cena.Actors.Cas;

/// <summary>
/// RDY-034: Thrown from CreateQuestionAsync / PersistAsync when the authored
/// answer fails CAS verification and the gate is in Enforce mode. Maps to HTTP 400.
/// </summary>
public sealed class CasVerificationFailedException : Exception
{
    public string ErrorCode => "CAS_VERIFICATION_FAILED";
    public string Details { get; }

    public CasVerificationFailedException(string details) : base(details)
    {
        Details = details;
    }
}

/// <summary>
/// RDY-034: Thrown from ApproveAsync when a math/physics question has no
/// Verified CAS binding and the gate is in Enforce mode. Maps to HTTP 409.
/// </summary>
public sealed class CasApprovalRejectedException : Exception
{
    public string ErrorCode => "CAS_VERIFICATION_REQUIRED";

    public CasApprovalRejectedException(string message) : base(message) { }
}
