// =============================================================================
// Cena Platform — Diagnostic Endpoint DTO shapes (RDY-023 + prr-228)
//
// Extracted from DiagnosticEndpoints.cs to keep both files under 500 LOC.
// The contracts here are wire-facing — changes must be compatible with any
// deployed student-web client, and the prr-228 per-target shapes MUST
// retain ProvenanceSource + ProvenanceLabel (ADR-0043 stamping; enforced
// by DiagnosticBlockProvenanceStampedTest).
// =============================================================================

namespace Cena.Student.Api.Host.Endpoints;

// ─────────────────────────────────────────────────────────────────────────────
// Legacy RDY-023 DTOs
// ─────────────────────────────────────────────────────────────────────────────

public record DiagnosticEstimateRequest(DiagnosticResponseItem[] Responses);

public record DiagnosticResponseItem(
    string QuestionId,
    string Subject,
    bool Correct,
    double Difficulty);

public record DiagnosticEstimateResponse(
    string StudentId,
    SubjectEstimate[] Estimates);

public record SubjectEstimate(
    string Subject,
    double Theta,
    double StandardError,
    double PInitial,
    int ItemsAnswered);

public record DiagnosticItemsResponse(DiagnosticItem[] Items);

public record DiagnosticItem(
    string QuestionId,
    string Subject,
    double Difficulty,
    string Band,
    string QuestionText,
    DiagnosticOption[] Options,
    string CorrectOptionKey);

public record DiagnosticOption(string Key, string Text);

// ─────────────────────────────────────────────────────────────────────────────
// prr-228 per-target DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-target diagnostic block response — served to the student client as
/// the single source of truth for "here are the items for this target".
/// </summary>
public sealed record PerTargetBlockResponse(
    string ExamTargetCode,
    PerTargetDiagnosticItem[] Items,
    int FloorCap,
    int CeilingCap);

/// <summary>
/// A single item inside a per-target block. <c>ProvenanceSource</c> and
/// <c>ProvenanceLabel</c> implement the ADR-0043 provenance stamping
/// requirement: every served item visibly declares its origin. Ministry
/// items never reach this contract.
/// </summary>
public sealed record PerTargetDiagnosticItem(
    string ItemId,
    string SkillCode,
    double DifficultyIrt,
    string Band,
    string QuestionText,
    DiagnosticOption[] Options,
    string CorrectOptionKey,
    string ProvenanceSource,
    string ProvenanceLabel);

/// <summary>Request body for the per-target submit endpoint.</summary>
public sealed record PerTargetBlockSubmitRequest(
    PerTargetBlockSubmitResponseItem[] Responses);

/// <summary>
/// A single response in the submit payload. <c>Skipped</c> is the explicit
/// "skip this item" affordance; a skip does not penalise the BKT posterior.
/// </summary>
public sealed record PerTargetBlockSubmitResponseItem(
    string ItemId,
    string SkillCode,
    bool Correct,
    bool Skipped,
    double DifficultyIrt);

/// <summary>
/// Submit endpoint response — carries the engine's summary for the client
/// to render the "calibration done" screen (no scores — calibration signal only).
/// </summary>
public sealed record PerTargetBlockSubmitResponse(
    string ExamTargetCode,
    int ItemsServed,
    int ItemsAnswered,
    int ItemsSkipped,
    string StopReason,
    PerTargetSkillPrior[] SkillPriors);

public sealed record PerTargetSkillPrior(string SkillCode, double PInitial);
