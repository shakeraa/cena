// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Marten Event Store Configuration
// Layer: Infrastructure Configuration | Runtime: .NET 9
// DB: PostgreSQL 16 + Marten v8.x
// ═══════════════════════════════════════════════════════════════════════

using JasperFx;
using JasperFx.Events;
using Marten;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Storage;
using Weasel.Core;
using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Questions;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Configuration;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten as the event store for the Cena platform.
    /// Call this from Program.cs / Startup via services.AddMarten(opts => opts.ConfigureCenaEventStore(connStr)).
    /// </summary>
    public static void ConfigureCenaEventStore(this StoreOptions opts, string connectionString)
    {
        opts.Connection(connectionString);
        opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;
        opts.DatabaseSchemaName = "cena";

        // ── Event Store Settings ──
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.MetadataConfig.EnableAll();
        opts.Events.TenancyStyle = Marten.Storage.TenancyStyle.Single;

        // ── Serialization: System.Text.Json with camelCase ──
        opts.UseSystemTextJsonForSerialization(
            enumStorage: Weasel.Core.EnumStorage.AsString,
            casing: Casing.CamelCase
        );

        // ── Register All Event Types (append-only, versioned) ──
        RegisterLearnerEvents(opts);
        RegisterPedagogyEvents(opts);
        RegisterEngagementEvents(opts);
        RegisterOutreachEvents(opts);
        RegisterQuestionEvents(opts);

        // ── Admin Document Types (BKD-002/003) ──
        opts.Schema.For<AdminUser>()
            .Identity(x => x.Id)
            .Index(x => x.Email)
            .Index(x => x.Role)
            .Index(x => x.Status)
            .Index(x => x.School);

        opts.Schema.For<CenaRoleDefinition>()
            .Identity(x => x.Id);

        // ── Question Read Model (inline projection for list queries) ──
        opts.Projections.Add<QuestionListProjection>(ProjectionLifecycle.Inline);
        opts.Schema.For<QuestionReadModel>()
            .Identity(x => x.Id)
            .Index(x => x.Subject)
            .Index(x => x.Status)
            .Index(x => x.BloomsLevel)
            .Index(x => x.Difficulty)
            .Index(x => x.QualityScore)
            .Index(x => x.Grade);

        // ── Pipeline Item Document (CNT-008: Ingestion Pipeline) ──
        opts.Schema.For<PipelineItemDocument>()
            .Identity(x => x.Id)
            .Index(x => x.CurrentStage)
            .Index(x => x.Status)
            .Index(x => x.ContentHash)
            .Index(x => x.SubmittedAt);

        // ── Moderation Audit Document (CNT-009: Moderation) ──
        opts.Schema.For<ModerationAuditDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Status)
            .Index(x => x.AssignedTo)
            .Index(x => x.Priority)
            .Index(x => x.Subject)
            .Index(x => x.SubmittedAt);

        // ── Content Document (SAI-06: Content Extraction Pipeline) ──
        opts.Schema.For<ContentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.PipelineItemId)
            .Index(x => x.Subject)
            .Index(x => x.Type)
            .Index(x => x.Language)
            .Index(x => x.AssociatedConceptId);

        // ── Content Block Document (SAI-05: Content Extraction Pipeline) ──
        opts.Schema.For<ContentBlockDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SourceDocId)
            .Index(x => x.ContentType)
            .Index(x => x.Subject)
            .Index(x => x.Language)
            .Index(x => x.Topic);

        // ── Tutoring Session Document (SAI-08) ──
        opts.Schema.For<TutoringSessionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.SessionId)
            .Index(x => x.StartedAt);

        // ── Register Tutoring Events (SAI-009) ──
        RegisterTutoringEvents(opts);

        // ── Register Ingestion Pipeline Events ──
        RegisterIngestionEvents(opts);

        // ── Snapshot Strategy: every 100 events per student ──
        // ACT-026: Inline snapshot projection — Marten auto-creates/updates snapshot
        // document on every SaveChangesAsync when event count crosses the threshold.
        opts.Projections.Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline);

        // Future projections — uncomment when projection types are available:
        // opts.Projections.Add<StudentMasteryProjection>(ProjectionLifecycle.Inline);
        // opts.Projections.Add<ClassOverviewProjection>(ProjectionLifecycle.Inline);
        // opts.Projections.Add<TeacherDashboardProjection>(ProjectionLifecycle.Async);
        // opts.Projections.Add<ParentProgressProjection>(ProjectionLifecycle.Async);
        // opts.Projections.Add<MethodologyEffectivenessProjection>(ProjectionLifecycle.Async);
        // opts.Projections.Add<RetentionCohortProjection>(ProjectionLifecycle.Async);
    }

    private static void RegisterLearnerEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<ConceptAttempted_V1>();
        opts.Events.AddEventType<ConceptMastered_V1>();
        opts.Events.AddEventType<MasteryDecayed_V1>();
        opts.Events.AddEventType<MethodologySwitched_V1>();
        opts.Events.AddEventType<StagnationDetected_V1>();
        opts.Events.AddEventType<AnnotationAdded_V1>();
        opts.Events.AddEventType<CognitiveLoadCooldownComplete_V1>();
    }

    private static void RegisterPedagogyEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<SessionStarted_V1>();
        opts.Events.AddEventType<SessionEnded_V1>();
        opts.Events.AddEventType<ExercisePresented_V1>();
        opts.Events.AddEventType<HintRequested_V1>();
        opts.Events.AddEventType<QuestionSkipped_V1>();
    }

    private static void RegisterEngagementEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<XpAwarded_V1>();
        opts.Events.AddEventType<StreakUpdated_V1>();
        opts.Events.AddEventType<BadgeEarned_V1>();
        opts.Events.AddEventType<StreakExpiring_V1>();
        opts.Events.AddEventType<ReviewDue_V1>();
    }

    private static void RegisterOutreachEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<OutreachMessageSent_V1>();
        opts.Events.AddEventType<OutreachMessageDelivered_V1>();
        opts.Events.AddEventType<OutreachResponseReceived_V1>();
    }

    private static void RegisterIngestionEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<FileReceived_V1>();
        opts.Events.AddEventType<OcrCompleted_V1>();
        opts.Events.AddEventType<QuestionsSegmented_V1>();
        opts.Events.AddEventType<QuestionsNormalized_V1>();
        opts.Events.AddEventType<QuestionsClassified_V1>();
        opts.Events.AddEventType<DeduplicationCompleted_V1>();
        opts.Events.AddEventType<QuestionsRecreated_V1>();
        opts.Events.AddEventType<PipelineStageFailed_V1>();
        opts.Events.AddEventType<MovedToReview_V1>();
        opts.Events.AddEventType<ContentExtracted_V1>();
        opts.Events.AddEventType<PipelineCompleted_V1>();
    }

    private static void RegisterTutoringEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<TutoringSessionStarted_V1>();
        opts.Events.AddEventType<TutoringMessageSent_V1>();
        opts.Events.AddEventType<TutoringSessionEnded_V1>();
        opts.Events.AddEventType<TutoringEpisodeCompleted_V1>();
    }

    private static void RegisterQuestionEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<QuestionAuthored_V1>();
        opts.Events.AddEventType<QuestionIngested_V1>();
        opts.Events.AddEventType<QuestionAiGenerated_V1>();
        opts.Events.AddEventType<QuestionStemEdited_V1>();
        opts.Events.AddEventType<QuestionOptionChanged_V1>();
        opts.Events.AddEventType<QuestionMetadataUpdated_V1>();
        opts.Events.AddEventType<QuestionQualityEvaluated_V1>();
        opts.Events.AddEventType<QuestionApproved_V1>();
        opts.Events.AddEventType<QuestionPublished_V1>();
        opts.Events.AddEventType<QuestionDeprecated_V1>();
        opts.Events.AddEventType<QuestionForked_V1>();
        opts.Events.AddEventType<ExplanationEdited_V1>();
        opts.Events.AddEventType<QuestionExplanationUpdated_V1>();
        opts.Events.AddEventType<LanguageVersionAdded_V1>();
    }
}
