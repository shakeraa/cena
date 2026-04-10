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
using Npgsql;
using Weasel.Core;
using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Projections;
using Cena.Actors.Questions;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.EventStore;
using Cena.Infrastructure.Seed;

namespace Cena.Actors.Configuration;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten using a shared NpgsqlDataSource (preferred).
    /// Ensures Marten shares the same connection pool as pgvector and other services.
    /// </summary>
    public static void ConfigureCenaEventStore(this StoreOptions opts, NpgsqlDataSource dataSource)
    {
        opts.Connection(dataSource);
        ConfigureCommon(opts);
    }

    /// <summary>
    /// Configures Marten with a raw connection string (legacy fallback).
    /// Marten creates its own internal pool — use NpgsqlDataSource overload when possible.
    /// </summary>
    public static void ConfigureCenaEventStore(this StoreOptions opts, string connectionString)
    {
        opts.Connection(connectionString);
        ConfigureCommon(opts);
    }

    private static void ConfigureCommon(StoreOptions opts)
    {
        opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.CreateOrUpdate;
        opts.DatabaseSchemaName = "cena";

        // ── Event Store Settings ──
        opts.Events.StreamIdentity = StreamIdentity.AsString;
        opts.Events.MetadataConfig.EnableAll();
        opts.Events.TenancyStyle = Marten.Storage.TenancyStyle.Single;

        // DATA-010: Optimistic concurrency — Marten v8 uses Quick append mode by default.
        // Concurrent writes to the same stream raise an exception caught by middleware/retry.

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
        RegisterFocusEvents(opts);

        // ── Register Upcasters (V(N) -> V(N+1) schema evolution, DATA-009) ──
        RegisterUpcasters(opts);

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

        // ── Analysis Job Document (job-based stagnation analysis) ──
        opts.Schema.For<AnalysisJobDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Status)
            .Index(x => x.DedupKey)
            .Index(x => x.RequestedBy)
            .Index(x => x.SubmittedAt);

        // ── Student Record Access Log (REV-013: FERPA compliance) ──
        opts.Schema.For<StudentRecordAccessLog>()
            .Identity(x => x.Id)
            .Index(x => x.AccessedBy)
            .Index(x => x.StudentId)
            .Index(x => x.AccessedAt);

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

        // REV-014: Index SchoolId for efficient tenant-scoped queries
        opts.Schema.For<StudentProfileSnapshot>().Index(x => x.SchoolId);

        // STB-01: Active session tracking
        opts.Projections.Snapshot<ActiveSessionSnapshot>(SnapshotLifecycle.Inline);

        // ── Student Preferences Document (STB-00b) ──
        opts.Schema.For<StudentPreferencesDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId);

        // ── Classroom Document (STB-00b) ──
        opts.Schema.For<ClassroomDocument>()
            .Identity(x => x.Id)
            .Index(x => x.JoinCode)
            .Index(x => x.TeacherId)
            .Index(x => x.SchoolId);

        // ── Device Session Document (STB-00b) ──
        opts.Schema.For<DeviceSessionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.LastSeenAt);

        // ── Share Token Document (STB-00b) ──
        opts.Schema.For<ShareTokenDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Token)
            .Index(x => x.ExpiresAt);

        // ── Tutor Thread Document (STB-04) ──
        opts.Schema.For<TutorThreadDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.UpdatedAt);

        // ── Tutor Message Document (STB-04) ──
        opts.Schema.For<TutorMessageDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ThreadId)
            .Index(x => x.StudentId)
            .Index(x => x.CreatedAt);

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
        opts.Events.AddEventType<ConceptAttempted_V2>(); // DATA-009: V2 with Duration field
        opts.Events.AddEventType<ConceptMastered_V1>();
        opts.Events.AddEventType<MasteryDecayed_V1>();
        opts.Events.AddEventType<MethodologySwitched_V1>();
        opts.Events.AddEventType<StagnationDetected_V1>();
        opts.Events.AddEventType<AnnotationAdded_V1>();
        opts.Events.AddEventType<CognitiveLoadCooldownComplete_V1>();

        // STB-01: Session lifecycle events
        opts.Events.AddEventType<LearningSessionStarted_V1>();
        opts.Events.AddEventType<LearningSessionEnded_V1>();
        opts.Events.AddEventType<OnboardingCompleted_V1>(); // STB-00
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

    private static void RegisterFocusEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<FocusScoreUpdated_V1>();
        opts.Events.AddEventType<MindWanderingDetected_V1>();
        opts.Events.AddEventType<MicrobreakSuggested_V1>();
        opts.Events.AddEventType<MicrobreakTaken_V1>();
        opts.Events.AddEventType<MicrobreakSkipped_V1>();
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

    /// <summary>
    /// DATA-009: Registers all event upcasters for schema evolution.
    /// Each upcaster transforms V(N) events to V(N+1) in-memory during stream reads.
    /// Add new upcasters here as event schemas evolve.
    /// </summary>
    private static void RegisterUpcasters(StoreOptions opts)
    {
        // ConceptAttempted: V1 -> V2 (adds Duration field, defaults to TimeSpan.Zero)
        opts.RegisterUpcaster(ConceptAttemptedV1ToV2Upcaster.Instance);
    }
}
