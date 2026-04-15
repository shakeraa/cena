// =============================================================================
// Cena Platform — CAS Gate Exceptions (RDY-034, ADR-0002)
// Thrown by QuestionBankService when the CAS ingestion gate rejects an
// operation in Enforce mode. Translated to 400/409 by the endpoint handler.
// =============================================================================

namespace Cena.Admin.Api.QualityGate;

/// <summary>
/// RDY-034: Thrown from CreateQuestionAsync when the authored answer fails
/// CAS verification and the gate is in Enforce mode. Maps to HTTP 400.
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
