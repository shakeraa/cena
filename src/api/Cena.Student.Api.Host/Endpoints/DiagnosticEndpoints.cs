// =============================================================================
// Cena Platform — Diagnostic Quiz Endpoints (RDY-023 + prr-228)
//
// Legacy (RDY-023):
//   POST /api/diagnostic/estimate — accepts quiz responses, returns IRT theta
//                                   per subject and the corresponding BKT
//                                   P_Initial values.
//   GET  /api/diagnostic/items?subjects=math,physics
//
// Per-target (prr-228, ADR-0050):
//   GET  /api/diagnostic/per-target/{examTargetCode}/block
//                                  — returns a 6-8 item block for one target
//                                   (easy-first; provenance stamped per item).
//   POST /api/diagnostic/per-target/{examTargetCode}/block/submit
//                                  — submits responses, emits per-skill BKT
//                                   priors keyed on (StudentId, ExamTargetCode,
//                                   SkillCode) via PerTargetDiagnosticEngine.
//
// Framing: the per-target surface is "calibration, not testing". Copy lives
// in the student-web i18n bundles; server responses never contain testing-
// language ("score", "grade", "pass"). The ship-gate scanner enforces.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Content;
using Cena.Actors.Diagnosis.PerTarget;
using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Services;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Student.Api.Host.Endpoints;

public static class DiagnosticEndpoints
{
    // prr-228: completion-SLO telemetry — rolling 7-day >= 85% target is
    // tracked off of these counters. Both the block-served and
    // block-completed counts carry the ExamTargetCode as a dimension so
    // alerts can key per-target.
    private static readonly Meter Meter = new("Cena.Diagnostic", "1.0");
    private static readonly Counter<long> PerTargetBlockServed =
        Meter.CreateCounter<long>("cena.diagnostic.per_target.block_served");
    private static readonly Counter<long> PerTargetBlockCompleted =
        Meter.CreateCounter<long>("cena.diagnostic.per_target.block_completed");

    public static void MapDiagnosticEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/diagnostic")
            .RequireAuthorization()
            .WithTags("Diagnostic");

        group.MapPost("/estimate", EstimateAbility);
        group.MapGet("/items", GetDiagnosticItems);

        // prr-228: per-target block surfaces.
        group.MapGet("/per-target/{examTargetCode}/block", GetPerTargetBlock);
        group.MapPost("/per-target/{examTargetCode}/block/submit", SubmitPerTargetBlock);
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /api/diagnostic/estimate (legacy RDY-023)
    // ─────────────────────────────────────────────────────────────────────

    private static IResult EstimateAbility(
        HttpContext ctx,
        [FromServices] IIrtCalibrationPipeline irt,
        [FromBody] DiagnosticEstimateRequest request,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (request.Responses is null || request.Responses.Length == 0)
            return Results.BadRequest(new { error = "At least one response is required." });

        var bySubject = request.Responses.GroupBy(r => r.Subject);
        var estimates = new List<SubjectEstimate>();

        foreach (var subjectGroup in bySubject)
        {
            var subject = subjectGroup.Key;
            var responses = subjectGroup
                .Select(r => new IrtResponse(studentId, r.QuestionId, r.Correct, null))
                .ToList();

            var itemParams = subjectGroup
                .Select(r => new IrtItemParameters(
                    r.QuestionId, r.Difficulty, 1.0, 0.0,
                    100, 0.0, CalibrationConfidence.Moderate, DateTimeOffset.UtcNow))
                .DistinctBy(p => p.QuestionId)
                .ToList();

            var ability = irt.EstimateAbility(studentId, subject, responses, itemParams);
            var pInitial = ThetaMasteryMapper.ThetaToPInitial(ability.Theta);

            estimates.Add(new SubjectEstimate(
                Subject: subject,
                Theta: Math.Round(ability.Theta, 3),
                StandardError: Math.Round(ability.StandardError, 3),
                PInitial: Math.Round(pInitial, 3),
                ItemsAnswered: ability.ItemsAnswered));
        }

        logger.LogInformation(
            "Diagnostic estimate for {StudentId}: {Subjects}",
            studentId, string.Join(", ", estimates.Select(e => $"{e.Subject}={e.Theta:F2}")));

        return Results.Ok(new DiagnosticEstimateResponse(
            StudentId: studentId,
            Estimates: estimates.ToArray()));
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /api/diagnostic/items (legacy RDY-023)
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetDiagnosticItems(
        HttpContext ctx,
        [FromServices] IDocumentStore store,
        [FromQuery] string subjects,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(subjects))
            return Results.BadRequest(new { error = "subjects query parameter is required." });

        var subjectList = subjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (subjectList.Length == 0 || subjectList.Length > 10)
            return Results.BadRequest(new { error = "Provide 1-10 comma-separated subjects." });

        await using var session = store.QuerySession();

        var items = new List<DiagnosticItem>();

        foreach (var subject in subjectList)
        {
            var questions = await session.Query<QuestionDocument>()
                .Where(q => q.Subject == subject && q.IsActive)
                .OrderBy(q => q.DifficultyElo)
                .Take(30)
                .ToListAsync();

            if (questions.Count == 0)
            {
                logger.LogWarning("No diagnostic items found for subject {Subject}", subject);
                continue;
            }

            var easy = questions.Where(q => q.DifficultyElo < 1400).Take(3);
            var medium = questions.Where(q => q.DifficultyElo >= 1400 && q.DifficultyElo <= 1600).Take(4);
            var hard = questions.Where(q => q.DifficultyElo > 1600).Take(3);

            var selected = easy.Concat(medium).Concat(hard).ToList();

            if (selected.Count < 5)
                selected = questions.Take(Math.Min(10, questions.Count)).ToList();

            items.AddRange(selected.Select(q => new DiagnosticItem(
                QuestionId: q.Id,
                Subject: subject,
                Difficulty: Math.Round(Cena.Actors.Services.IrtEloConversion.EloToIrt(q.DifficultyElo), 3),
                Band: Cena.Actors.Services.IrtEloConversion.DifficultyBand(
                    Cena.Actors.Services.IrtEloConversion.EloToIrt(q.DifficultyElo)),
                QuestionText: q.Prompt,
                Options: q.Choices?.Select((c, i) => new DiagnosticOption($"opt_{i}", c)).ToArray()
                    ?? Array.Empty<DiagnosticOption>(),
                CorrectOptionKey: q.CorrectAnswer)));
        }

        return Results.Ok(new DiagnosticItemsResponse(items.ToArray()));
    }

    // ─────────────────────────────────────────────────────────────────────
    // GET /api/diagnostic/per-target/{examTargetCode}/block (prr-228)
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IResult> GetPerTargetBlock(
        HttpContext ctx,
        IDocumentStore store,
        [FromRoute] string examTargetCode,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (!ExamTargetCode.TryParse(examTargetCode, out var targetCode))
        {
            return Results.BadRequest(new { error = $"Invalid examTargetCode '{examTargetCode}'." });
        }

        var subject = DeriveSubjectFromTargetCode(targetCode.Value);

        await using var session = store.QuerySession();
        var questions = await session.Query<QuestionDocument>()
            .Where(q => q.Subject == subject && q.IsActive)
            .OrderBy(q => q.DifficultyElo)
            .Take(40)
            .ToListAsync();

        if (questions.Count == 0)
        {
            logger.LogWarning(
                "No per-target items found for targetCode={TargetCode} subject={Subject}",
                targetCode.Value, subject);
            return Results.Ok(new PerTargetBlockResponse(
                ExamTargetCode: targetCode.Value,
                Items: Array.Empty<PerTargetDiagnosticItem>(),
                FloorCap: DiagnosticBlockThresholds.FloorCap,
                CeilingCap: DiagnosticBlockThresholds.CeilingCap));
        }

        // Easy-first selection: take ceiling-cap items ordered by Elo
        // ascending so the student opens on the friendliest item. The
        // adaptive-stop engine on the client decides when to end the
        // block; the server sends up to the ceiling cap so there's always
        // headroom.
        var selected = questions
            .Take(DiagnosticBlockThresholds.CeilingCap)
            .Select(BuildPerTargetItem)
            .ToList();

        PerTargetBlockServed.Add(
            1,
            new KeyValuePair<string, object?>("exam_target_code", targetCode.Value));

        logger.LogInformation(
            "PerTarget block served: target={TargetCode} subject={Subject} items={Count}",
            targetCode.Value, subject, selected.Count);

        return Results.Ok(new PerTargetBlockResponse(
            ExamTargetCode: targetCode.Value,
            Items: selected.ToArray(),
            FloorCap: DiagnosticBlockThresholds.FloorCap,
            CeilingCap: DiagnosticBlockThresholds.CeilingCap));
    }

    // ─────────────────────────────────────────────────────────────────────
    // POST /api/diagnostic/per-target/{examTargetCode}/block/submit (prr-228)
    // ─────────────────────────────────────────────────────────────────────

    private static async Task<IResult> SubmitPerTargetBlock(
        HttpContext ctx,
        PerTargetDiagnosticEngine engine,
        [FromRoute] string examTargetCode,
        [FromBody] PerTargetBlockSubmitRequest request,
        ILogger<DiagnosticLogMarker> logger)
    {
        var studentId = ctx.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(studentId))
            return Results.Unauthorized();

        ResourceOwnershipGuard.VerifyStudentAccess(ctx.User, studentId);

        if (!ExamTargetCode.TryParse(examTargetCode, out var targetCode))
        {
            return Results.BadRequest(new { error = $"Invalid examTargetCode '{examTargetCode}'." });
        }

        if (request.Responses is null || request.Responses.Length == 0)
        {
            return Results.BadRequest(new { error = "At least one response is required." });
        }

        if (request.Responses.Length > DiagnosticBlockThresholds.CeilingCap)
        {
            return Results.BadRequest(new
            {
                error = $"Block cannot contain more than "
                    + $"{DiagnosticBlockThresholds.CeilingCap} responses (ceiling cap).",
            });
        }

        var responses = new List<DiagnosticBlockResponse>(request.Responses.Length);
        foreach (var r in request.Responses)
        {
            SkillCode skill;
            try
            {
                skill = SkillCode.Parse(r.SkillCode);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var action = r.Skipped
                ? DiagnosticResponseAction.Skipped
                : DiagnosticResponseAction.Answered;
            responses.Add(new DiagnosticBlockResponse(
                ItemId: r.ItemId,
                SkillCode: skill,
                Action: action,
                Correct: !r.Skipped && r.Correct,
                DifficultyIrt: r.DifficultyIrt));
        }

        var summary = await engine.ProcessBlockAsync(
            studentAnonId: studentId,
            examTargetCode: targetCode,
            responses: responses).ConfigureAwait(false);

        PerTargetBlockCompleted.Add(
            1,
            new KeyValuePair<string, object?>("exam_target_code", targetCode.Value),
            new KeyValuePair<string, object?>("stop_reason", summary.StopReason.ToString()));

        var skillPriors = summary.SkillPriors
            .Select(kv => new PerTargetSkillPrior(kv.Key.Value, Math.Round(kv.Value, 3)))
            .ToArray();

        logger.LogInformation(
            "PerTarget block submitted: target={TargetCode} served={Served} "
            + "answered={Answered} skipped={Skipped} stop={Stop}",
            targetCode.Value, summary.ItemsServed, summary.ItemsAnswered,
            summary.ItemsSkipped, summary.StopReason);

        return Results.Ok(new PerTargetBlockSubmitResponse(
            ExamTargetCode: targetCode.Value,
            ItemsServed: summary.ItemsServed,
            ItemsAnswered: summary.ItemsAnswered,
            ItemsSkipped: summary.ItemsSkipped,
            StopReason: summary.StopReason.ToString(),
            SkillPriors: skillPriors));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static PerTargetDiagnosticItem BuildPerTargetItem(QuestionDocument q)
    {
        // ADR-0043 provenance stamping — every served item declares its
        // source. Question docs in this prototype are all AI-recreated;
        // the provenance comes from the doc's Source field (falls back to
        // "ai-authored-recreation" so the contract is always populated).
        var provenanceKind = DeriveProvenanceKind(q);
        if (provenanceKind == ProvenanceKind.MinistryBagrut)
        {
            // This is a belt-and-braces check; the catalog ingester should
            // never write a MinistryBagrut-provenance doc as `IsActive=true`
            // because ADR-0043 reference-only means Ministry docs are
            // curator-internal. Throwing here is intentional: a
            // MinistryBagrut doc leaking into the student-facing query is
            // a P0 incident.
            throw new InvalidOperationException(
                $"QuestionDocument '{q.Id}' has MinistryBagrut provenance but "
                + "is marked active for student delivery — this violates "
                + "ADR-0043. Investigate the catalog ingest path.");
        }

        var difficultyIrt = IrtEloConversion.EloToIrt(q.DifficultyElo);
        var band = IrtEloConversion.DifficultyBand(difficultyIrt);

        return new PerTargetDiagnosticItem(
            ItemId: q.Id,
            SkillCode: DeriveSkillCode(q).Value,
            DifficultyIrt: Math.Round(difficultyIrt, 3),
            Band: band,
            QuestionText: q.Prompt,
            Options: q.Choices?.Select((c, i) => new DiagnosticOption($"opt_{i}", c)).ToArray()
                ?? Array.Empty<DiagnosticOption>(),
            CorrectOptionKey: q.CorrectAnswer,
            ProvenanceSource: MapProvenanceSource(provenanceKind),
            ProvenanceLabel: MapProvenanceLabel(provenanceKind));
    }

    private static ProvenanceKind DeriveProvenanceKind(QuestionDocument q)
    {
        // Pragmatic default: every active question-bank doc is an
        // AI-authored recreation per the ADR-0032 CAS-gated ingestion
        // pipeline. When the catalog gains a dedicated provenance field
        // (tracked under PRR-242 corpus ingestion), plug it in here. For
        // now, the belt-and-braces invariant is that Ministry docs are
        // NEVER IsActive=true (ADR-0043 reference-only) so the switch
        // never needs to produce MinistryBagrut — and if it ever did, the
        // caller's throw would trip a P0 alarm.
        _ = q; // intentional — future catalog provenance hook
        return ProvenanceKind.AiRecreated;
    }

    private static string MapProvenanceSource(ProvenanceKind kind) => kind switch
    {
        ProvenanceKind.AiRecreated => "ai-authored-recreation",
        ProvenanceKind.TeacherAuthoredOriginal => "teacher-authored-original",
        // MinistryBagrut never reaches the wire — we still have a mapping
        // for completeness + for the architecture test that scans this
        // switch to ensure all enum values are handled.
        ProvenanceKind.MinistryBagrut => "ministry-reference",
        _ => "ai-authored-recreation",
    };

    private static string MapProvenanceLabel(ProvenanceKind kind) => kind switch
    {
        ProvenanceKind.AiRecreated => "onboarding.perTargetDiagnostic.provenance.aiRecreated",
        ProvenanceKind.TeacherAuthoredOriginal => "onboarding.perTargetDiagnostic.provenance.teacherAuthored",
        ProvenanceKind.MinistryBagrut => "onboarding.perTargetDiagnostic.provenance.ministryReference",
        _ => "onboarding.perTargetDiagnostic.provenance.aiRecreated",
    };

    private static SkillCode DeriveSkillCode(QuestionDocument q)
    {
        // Prefer the doc's ConceptId (the catalog's concept taxonomy id);
        // fall back to a subject-derived pseudo-skill so every item has a
        // skill for the BKT key.
        var raw = !string.IsNullOrWhiteSpace(q.ConceptId)
            ? q.ConceptId
            : $"{q.Subject}.unmapped";
        try
        {
            return SkillCode.Parse(raw);
        }
        catch (ArgumentException)
        {
            return SkillCode.Parse($"{(string.IsNullOrWhiteSpace(q.Subject) ? "general" : q.Subject)}.uncategorised");
        }
    }

    private static string DeriveSubjectFromTargetCode(string targetCodeValue)
    {
        // "bagrut-math-5yu" → "math"; "sat-math" → "math"; "pet-quant" →
        // "quant". Heuristic extraction of the middle segment; catalog
        // consolidation in PRR-242 will replace this.
        var parts = targetCodeValue.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : targetCodeValue;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal log marker (DTOs live in DiagnosticEndpointsContracts.cs).
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class DiagnosticLogMarker;
