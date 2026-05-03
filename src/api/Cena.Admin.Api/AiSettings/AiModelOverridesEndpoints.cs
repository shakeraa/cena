// =============================================================================
// Cena Platform — /api/admin/ai/settings/model-overrides endpoints
//
//   GET  /api/admin/ai/settings/model-overrides
//        → snapshot of every task + currently resolved model + source tier
//          + supported-models list (with cost-per-Mtok metadata) + last
//          changed metadata. Backs the admin SPA's "Advanced — per-task
//          model overrides" panel.
//
//   PUT  /api/admin/ai/settings/model-overrides/{taskName}
//        body: {"modelId": "claude-sonnet-4-6"}   → set override
//        body: {"modelId": null}                  → clear override
//        body: {"modelId": ""}                    → clear override (alias)
//
//        - 200 on success
//        - 400 when modelId is non-null AND not in AnthropicSupportedModels
//          (closed-set discipline mirrors the rest of admin write surfaces)
//        - 400 when taskName is not in RoutingConfigTaskDefaults.KnownTaskNames
//          (prevents typo-overriding a task that has no caller)
//        - 401 / 403 via auth policy (AdminOnly)
//
//        Side effects:
//          - mutates AiSettingsDocument.ModelOverridesByTask
//          - appends an AiSettingsChangedEvent to the singleton's event stream
//          - invalidates the IModelResolver cache so the next call hits
//            the new value within milliseconds (vs the 60s TTL window)
//
// AuthZ: AdminOnly. The override surface is a model-routing decision —
// finance / product / engineering policy — not a moderation surface, so
// it sits one level above the AI generation rate-limit bucket
// (ModeratorOrAbove). Pulling cost levers needs admin clearance.
// =============================================================================

using System.Text.Json.Serialization;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.AiSettings;

public sealed record SupportedModelView(
    string ModelId,
    string DisplayName,
    decimal InputUsdPerMtok,
    decimal OutputUsdPerMtok,
    string Tier);

public sealed record TaskModelView(
    string Task,
    string CurrentModelId,
    string Source,
    bool IsOverridden,
    string? OverrideModelId,
    string? Description);

public sealed record ModelOverridesResponse(
    IReadOnlyList<SupportedModelView> SupportedModels,
    IReadOnlyList<TaskModelView> Tasks,
    string? LastChangedBy,
    DateTimeOffset? LastChangedAt,
    string? GlobalDefaultModelId);

public sealed record SetModelOverrideRequest([property: JsonPropertyName("modelId")] string? ModelId);

public static class AiModelOverridesEndpoints
{
    public const string GetRoute = "/api/admin/ai/settings/model-overrides";
    public const string PutRoute = "/api/admin/ai/settings/model-overrides/{taskName}";

    /// <summary>
    /// Plain-language descriptions used by the admin SPA panel. Keys must
    /// match canonical task names (see RoutingConfigTaskDefaults). Adding a
    /// new task means adding a row here AND under
    /// contracts/llm/routing-config.yaml § default_model_by_task:.
    /// </summary>
    private static readonly Dictionary<string, string> TaskDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["question_generation"] =
            "AI question generation (admin tool, tier 3, tool-use Bagrut-aligned batches).",
        ["ocr_text_enhance"] =
            "OCR cleanup pass on Bagrut draft text (math wrap, paragraph breaks, figure markers).",
        ["concept_extraction"] =
            "Multi-concept canonical extraction from question text (ADR-0062 Phase 1, tier 2).",
        ["quality_gate"] =
            "0-100 rubric scoring of every generated question (factual / language / pedagogical, tier 2).",
        ["bagrut_segmentation"] =
            "Hebrew Bagrut PDF question-boundary detection (one Haiku call per PDF, tier 2).",
    };

    public static IEndpointRouteBuilder MapAiModelOverridesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(GetRoute, HandleGetAsync)
            .WithName("GetAiModelOverrides")
            .WithTags("AI Settings")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        app.MapPut(PutRoute, HandlePutAsync)
            .WithName("PutAiModelOverride")
            .WithTags("AI Settings")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        return app;
    }

    internal static async Task<IResult> HandleGetAsync(
        IModelResolver resolver,
        IDocumentStore documentStore,
        RoutingConfigTaskDefaults yamlDefaults,
        CancellationToken ct)
    {
        var resolutions = await resolver.SnapshotAsync(ct);

        // Project AnthropicSupportedModels into the SPA-shaped view.
        var supported = AnthropicSupportedModels.All
            .Select(m => new SupportedModelView(
                m.ModelId,
                m.DisplayName,
                m.InputUsdPerMtok,
                m.OutputUsdPerMtok,
                m.Tier))
            .ToList();

        // Project resolver snapshot rows + plain-language descriptions.
        var tasks = resolutions.Select(r =>
        {
            TaskDescriptions.TryGetValue(r.Task, out var desc);
            return new TaskModelView(
                Task: r.Task,
                CurrentModelId: r.CurrentModelId,
                Source: r.Source,
                IsOverridden: r.IsOverridden,
                OverrideModelId: r.OverrideModelId,
                Description: desc);
        }).ToList();

        // Last-changed metadata sourced from the doc itself (separate from
        // the global UpdatedBy/UpdatedAt which fires on any settings PUT).
        AiSettingsDocument? doc = null;
        try
        {
            await using var session = documentStore.QuerySession();
            doc = await session.LoadAsync<AiSettingsDocument>(
                AiSettingsDocument.SingletonId, ct);
        }
        catch (Marten.Exceptions.MartenCommandException ex)
            when (ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "42P01")
        {
            // Cold start — table not auto-created yet. Empty audit metadata
            // is correct: nothing has been changed because nothing exists.
        }

        return Results.Ok(new ModelOverridesResponse(
            SupportedModels: supported,
            Tasks: tasks,
            LastChangedBy: doc?.ModelOverridesLastChangedBy,
            LastChangedAt: doc?.ModelOverridesLastChangedAt,
            GlobalDefaultModelId: yamlDefaults.GlobalDefaultModelId));
    }

    internal static async Task<IResult> HandlePutAsync(
        string taskName,
        [FromBody] SetModelOverrideRequest request,
        HttpContext httpContext,
        IDocumentStore documentStore,
        IModelResolver resolver,
        RoutingConfigTaskDefaults yamlDefaults,
        ILogger<ModelResolver> logger,
        CancellationToken ct)
    {
        // 1. taskName closed-set check.
        if (string.IsNullOrWhiteSpace(taskName))
        {
            return Results.BadRequest(new
            {
                error = "taskName route parameter is required.",
                category = "MISSING_TASK_NAME",
            });
        }
        if (!yamlDefaults.KnownTaskNames.Contains(taskName, StringComparer.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = $"Unknown task name '{taskName}'. " +
                        $"Add a row under contracts/llm/routing-config.yaml § default_model_by_task: " +
                        $"before configuring an override. Known tasks: " +
                        $"[{string.Join(", ", yamlDefaults.KnownTaskNames)}].",
                category = "UNKNOWN_TASK",
            });
        }

        // 2. modelId validation. Null/empty/whitespace = clear override.
        //    Non-null = closed-set check against AnthropicSupportedModels.
        var requestedModelId = request?.ModelId;
        var clearing = string.IsNullOrWhiteSpace(requestedModelId);
        if (!clearing && !AnthropicSupportedModels.IsSupported(requestedModelId))
        {
            return Results.BadRequest(new
            {
                error = $"Unsupported model id '{requestedModelId}'. " +
                        "Pick a value from the supportedModels list returned by GET " +
                        "/api/admin/ai/settings/model-overrides.",
                category = "UNSUPPORTED_MODEL",
            });
        }

        var changedBy = httpContext.User.FindFirst("sub")?.Value
                        ?? httpContext.User.Identity?.Name
                        ?? "anonymous";
        var changedAt = DateTimeOffset.UtcNow;

        // 3. Persist the change atomically: document mutation + event
        //    stream append + cache invalidation. The Marten session
        //    SaveChangesAsync is a single transaction so the doc and
        //    the audit event commit together — neither can land
        //    without the other.
        await using var session = documentStore.LightweightSession();
        var doc = await session.LoadAsync<AiSettingsDocument>(
            AiSettingsDocument.SingletonId, ct).ConfigureAwait(false);
        doc ??= new AiSettingsDocument { Id = AiSettingsDocument.SingletonId };

        // Capture old value for audit BEFORE mutating.
        doc.ModelOverridesByTask.TryGetValue(taskName, out var oldModelId);
        var hadOldValue = !string.IsNullOrWhiteSpace(oldModelId);

        // Idempotency: if the curator submits the same value as already
        // persisted (clearing-already-clear, or setting-to-existing), we
        // still mutate UpdatedAt fields but emit a no-op event so the
        // audit trail captures the intent. This is intentional — an
        // operator clicking "Apply" twice is signal worth preserving.

        if (clearing)
        {
            doc.ModelOverridesByTask.Remove(taskName);
        }
        else
        {
            doc.ModelOverridesByTask[taskName] = requestedModelId!;
        }

        doc.ModelOverridesLastChangedBy = changedBy;
        doc.ModelOverridesLastChangedAt = changedAt;
        doc.UpdatedAt = changedAt;
        doc.UpdatedBy = changedBy;

        session.Store(doc);
        session.Events.Append(
            AiSettingsDocument.SingletonId,
            new AiSettingsChangedEvent(
                TaskName: taskName,
                OldModelId: hadOldValue ? oldModelId : null,
                NewModelId: clearing ? null : requestedModelId,
                ChangedBy: changedBy,
                ChangedAt: changedAt));
        await session.SaveChangesAsync(ct).ConfigureAwait(false);

        // 4. Drop the resolver cache so in-flight calls see the new value.
        resolver.Invalidate();

        logger.LogInformation(
            "[AUDIT] AiModelOverride changed: task={Task} oldModel={Old} newModel={New} by={ChangedBy}",
            taskName,
            hadOldValue ? oldModelId : "(none)",
            clearing ? "(cleared)" : requestedModelId,
            changedBy);

        // Resolve the post-change current model so the SPA can update its
        // row without a follow-up GET round-trip.
        var newCurrent = await resolver.ResolveModelForTaskAsync(taskName, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            task = taskName,
            currentModelId = newCurrent,
            source = clearing ? "routing-config-task-default" : "override",
            isOverridden = !clearing,
            overrideModelId = clearing ? null : requestedModelId,
            changedBy,
            changedAt,
        });
    }
}
