// =============================================================================
// Cena Platform -- NATS Bus Message Validator (SEC-004)
// Validates incoming NATS message payloads before routing to Proto.Actor cluster.
// Synchronous validation, no I/O. Rejects malformed messages with structured errors.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Bus;

/// <summary>
/// Validates NATS bus message payloads before they are dispatched to actors.
/// All methods return a <see cref="BusValidationResult"/> — never throw.
/// </summary>
public static class BusMessageValidator
{
    // Identifier constraints: UUIDs are 32 hex + 4 dashes = 36 chars.
    // Firebase UIDs are up to 128 chars (alphanumeric).
    private const int MaxIdLength = 128;
    private const int MaxSubjectLength = 200;
    private const int MaxAnnotationLength = 5000;
    private const int MaxSessionReasonLength = 64;
    private const int MaxMethodologyLength = 64;
    private const int MaxDeviceTypeLength = 64;
    private const int MaxAppVersionLength = 32;

    private static readonly HashSet<string> ValidAnnotationKinds =
        new(StringComparer.OrdinalIgnoreCase) { "note", "question", "confusion", "insight" };

    private static readonly HashSet<string> ValidSessionEndReasons =
        new(StringComparer.OrdinalIgnoreCase) { "completed", "timeout", "user_exit", "error", "session_expired" };

    // Accepts snake_case, kebab-case, and concatenated forms so emitters
    // don't all need to canonicalise on the way in. The emulator +
    // SimulationEventSeeder use the underscore form; earlier content
    // code used the no-underscore form. Both are valid here.
    private static readonly HashSet<string> ValidQuestionTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "multiple_choice", "multiplechoice", "multiple-choice",
            "short_answer",    "shortanswer",    "short-answer",
            "true_false",      "truefalse",      "true-false",
            "numeric",
            "ordering",
            "free_response",   "freeresponse",   "free-response",
            "proof",
        };

    // ── Public validate methods ──

    public static BusValidationResult Validate(BusStartSession msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("StartSession.StudentId is invalid or too long");

        if (!IsValidId(msg.SubjectId))
            return BusValidationResult.Reject("StartSession.SubjectId is invalid or too long");

        if (msg.ConceptId is not null && !IsValidId(msg.ConceptId))
            return BusValidationResult.Reject("StartSession.ConceptId is invalid");

        if (string.IsNullOrWhiteSpace(msg.DeviceType) || msg.DeviceType.Length > MaxDeviceTypeLength)
            return BusValidationResult.Reject("StartSession.DeviceType is missing or too long");

        if (string.IsNullOrWhiteSpace(msg.AppVersion) || msg.AppVersion.Length > MaxAppVersionLength)
            return BusValidationResult.Reject("StartSession.AppVersion is missing or too long");

        if (msg.SchoolId is not null && !IsValidId(msg.SchoolId))
            return BusValidationResult.Reject("StartSession.SchoolId is invalid");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusEndSession msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("EndSession.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("EndSession.SessionId is invalid");

        if (string.IsNullOrWhiteSpace(msg.Reason) || msg.Reason.Length > MaxSessionReasonLength)
            return BusValidationResult.Reject("EndSession.Reason is missing or too long");

        if (!ValidSessionEndReasons.Contains(msg.Reason))
            return BusValidationResult.Reject($"EndSession.Reason '{msg.Reason}' is not a recognised value");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusResumeSession msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("ResumeSession.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("ResumeSession.SessionId is invalid");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusGetSessionSnapshot msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("GetSessionSnapshot.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("GetSessionSnapshot.SessionId is invalid");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusConceptAttempt msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("ConceptAttempt.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("ConceptAttempt.SessionId is invalid");

        if (!IsValidId(msg.ConceptId))
            return BusValidationResult.Reject("ConceptAttempt.ConceptId is invalid");

        if (!IsValidId(msg.QuestionId))
            return BusValidationResult.Reject("ConceptAttempt.QuestionId is invalid");

        if (string.IsNullOrWhiteSpace(msg.QuestionType) || msg.QuestionType.Length > 64)
            return BusValidationResult.Reject("ConceptAttempt.QuestionType is missing or too long");

        if (!ValidQuestionTypes.Contains(msg.QuestionType))
            return BusValidationResult.Reject($"ConceptAttempt.QuestionType '{msg.QuestionType}' is not recognised");

        if (msg.ResponseTimeMs < 0 || msg.ResponseTimeMs > 600_000) // 10-minute cap
            return BusValidationResult.Reject("ConceptAttempt.ResponseTimeMs is out of range [0, 600000]");

        if (msg.HintCountUsed < 0 || msg.HintCountUsed > 50)
            return BusValidationResult.Reject("ConceptAttempt.HintCountUsed is out of range [0, 50]");

        if (msg.BackspaceCount < 0 || msg.BackspaceCount > 10_000)
            return BusValidationResult.Reject("ConceptAttempt.BackspaceCount is out of range");

        if (msg.AnswerChangeCount < 0 || msg.AnswerChangeCount > 10_000)
            return BusValidationResult.Reject("ConceptAttempt.AnswerChangeCount is out of range");

        if (string.IsNullOrWhiteSpace(msg.Answer) || msg.Answer.Length > 5000)
            return BusValidationResult.Reject("ConceptAttempt.Answer is missing or exceeds 5000 chars");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusAddAnnotation msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("AddAnnotation.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("AddAnnotation.SessionId is invalid");

        if (!IsValidId(msg.ConceptId))
            return BusValidationResult.Reject("AddAnnotation.ConceptId is invalid");

        if (string.IsNullOrWhiteSpace(msg.Text))
            return BusValidationResult.Reject("AddAnnotation.Text is empty");

        if (msg.Text.Length > MaxAnnotationLength)
            return BusValidationResult.Reject($"AddAnnotation.Text exceeds {MaxAnnotationLength} chars");

        if (string.IsNullOrWhiteSpace(msg.Kind) || !ValidAnnotationKinds.Contains(msg.Kind))
            return BusValidationResult.Reject($"AddAnnotation.Kind '{msg.Kind}' is not a recognised value");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusMethodologySwitch msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("MethodologySwitch.StudentId is invalid");

        if (!IsValidId(msg.SessionId))
            return BusValidationResult.Reject("MethodologySwitch.SessionId is invalid");

        if (string.IsNullOrWhiteSpace(msg.FromMethodology) || msg.FromMethodology.Length > MaxMethodologyLength)
            return BusValidationResult.Reject("MethodologySwitch.FromMethodology is missing or too long");

        if (string.IsNullOrWhiteSpace(msg.ToMethodology) || msg.ToMethodology.Length > MaxMethodologyLength)
            return BusValidationResult.Reject("MethodologySwitch.ToMethodology is missing or too long");

        if (string.IsNullOrWhiteSpace(msg.Reason) || msg.Reason.Length > MaxSubjectLength)
            return BusValidationResult.Reject("MethodologySwitch.Reason is missing or too long");

        return BusValidationResult.Ok();
    }

    public static BusValidationResult Validate(BusAccountStatusChanged msg)
    {
        if (!IsValidId(msg.StudentId))
            return BusValidationResult.Reject("AccountStatusChanged.StudentId is invalid");

        if (string.IsNullOrWhiteSpace(msg.NewStatus) || msg.NewStatus.Length > 32)
            return BusValidationResult.Reject("AccountStatusChanged.NewStatus is missing or too long");

        if (string.IsNullOrWhiteSpace(msg.ChangedBy))
            return BusValidationResult.Reject("AccountStatusChanged.ChangedBy is required");

        return BusValidationResult.Ok();
    }

    // ── Envelope-level validation ──

    public static BusValidationResult ValidateEnvelope<T>(BusEnvelope<T> envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope.MessageId) || envelope.MessageId.Length > 64)
            return BusValidationResult.Reject("Envelope.MessageId is missing or too long");

        if (string.IsNullOrWhiteSpace(envelope.Source) || envelope.Source.Length > 64)
            return BusValidationResult.Reject("Envelope.Source is missing or too long");

        if (envelope.Timestamp == default || envelope.Timestamp > DateTimeOffset.UtcNow.AddMinutes(5))
            return BusValidationResult.Reject("Envelope.Timestamp is invalid or in the future");

        if (envelope.Payload is null)
            return BusValidationResult.Reject("Envelope.Payload is null");

        return BusValidationResult.Ok();
    }

    // ── Helpers ──

    /// <summary>
    /// An ID is valid if it is non-empty, within the length limit,
    /// and contains only alphanumeric characters, hyphens, underscores.
    /// </summary>
    private static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > MaxIdLength)
            return false;

        foreach (var c in id)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }

        return true;
    }
}

/// <summary>
/// Result of a bus message validation check.
/// </summary>
public sealed record BusValidationResult(bool IsValid, string? RejectionReason)
{
    public static BusValidationResult Ok() => new(true, null);
    public static BusValidationResult Reject(string reason) => new(false, reason);
}

/// <summary>
/// Extension methods to integrate BusMessageValidator into NatsBusRouter logging.
/// </summary>
public static class BusMessageValidatorExtensions
{
    /// <summary>
    /// Logs a rejected message and returns false.
    /// </summary>
    public static bool LogIfRejected(
        this BusValidationResult result,
        ILogger logger,
        string subject,
        string? messageId = null)
    {
        if (!result.IsValid)
        {
            logger.LogWarning(
                "NATS message rejected on {Subject} (msgId={MsgId}): {Reason}",
                subject, messageId ?? "(unknown)", result.RejectionReason);
            return false;
        }
        return true;
    }
}
