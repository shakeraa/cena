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
using Cena.Actors.Audit;
using Cena.Actors.Diagnosis;
using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Cena.Actors.Projections;
using Cena.Actors.Questions;
using Cena.Actors.Serving;
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
    /// <param name="autoCreateMode">
    /// DB-03: AutoCreate mode from config. Defaults to "CreateOrUpdate" for backwards compatibility.
    /// Production/Staging/Test should use "None" — schema changes go through the migrator (DB-02).
    /// </param>
    public static void ConfigureCenaEventStore(this StoreOptions opts, NpgsqlDataSource dataSource, string autoCreateMode = "CreateOrUpdate")
    {
        opts.Connection(dataSource);
        ConfigureCommon(opts, autoCreateMode);
    }

    /// <summary>
    /// Configures Marten with a raw connection string (legacy fallback).
    /// Marten creates its own internal pool — use NpgsqlDataSource overload when possible.
    /// </summary>
    /// <param name="autoCreateMode">
    /// DB-03: AutoCreate mode from config. Defaults to "CreateOrUpdate" for backwards compatibility.
    /// Production/Staging/Test should use "None" — schema changes go through the migrator (DB-02).
    /// </param>
    public static void ConfigureCenaEventStore(this StoreOptions opts, string connectionString, string autoCreateMode = "CreateOrUpdate")
    {
        opts.Connection(connectionString);
        ConfigureCommon(opts, autoCreateMode);
    }

    private static void ConfigureCommon(StoreOptions opts, string autoCreateMode = "CreateOrUpdate")
    {
        // DB-03: AutoCreate mode is now configurable per environment.
        // Production/Staging/Test = "None" (schema changes via migrator only).
        // Development = "CreateOrUpdate" (speeds local iteration).
        opts.AutoCreateSchemaObjects = autoCreateMode switch
        {
            "None" => JasperFx.AutoCreate.None,
            "CreateOnly" => JasperFx.AutoCreate.CreateOnly,
            "CreateOrUpdate" => JasperFx.AutoCreate.CreateOrUpdate,
            "All" => JasperFx.AutoCreate.All,
            _ => throw new InvalidOperationException(
                $"Unknown Marten AutoCreate mode: \'{autoCreateMode}\'. " +
                "Valid values: None, CreateOnly, CreateOrUpdate, All.")
        };
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
        RegisterNotificationEvents(opts); // FIND-data-002: Was defined but never called
        RegisterMessagingEvents(opts); // FIND-data-012: Register messaging events
        RegisterEnrollmentEvents(opts); // TENANCY-P1c: Enrollment lifecycle events
        RegisterMisconceptionEvents(opts); // RDY-006 / ADR-0003: ML-excluded misconception events

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

        // HARDEN SocialEndpoints: Async projection for class feed items from events
        opts.Projections.Add<ClassFeedItemProjection>(ProjectionLifecycle.Async);

        // FIND-data-024: Security audit projection for compliance/forensics
        opts.Projections.Add<SecurityAuditProjection>(ProjectionLifecycle.Async);
        opts.Schema.For<AuditEventDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Timestamp)
            .Index(x => x.UserId)
            .Index(x => x.TenantId)
            .Index(x => x.Action)
            .Index(x => x.EventType);

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

        // ── Question CAS Binding (ADR-0032 §4 / RDY-049) ──
        // Unique index on (QuestionId, CorrectAnswerHash) gives us
        // concurrency-safe idempotency: two racing verify calls on the
        // same (question, answer-hash) cannot both insert. The second
        // write hits the unique constraint and the caller falls back to
        // the already-stored binding.
        opts.Schema.For<Cena.Infrastructure.Documents.QuestionCasBinding>()
            .Identity(x => x.Id)
            .Index(x => x.QuestionId)
            .Index(x => x.Status)
            .UniqueIndex(
                Marten.Schema.UniqueIndexType.Computed,
                "uniq_question_cas_binding_qid_hash",
                x => x.QuestionId,
                x => x.CorrectAnswerHash);

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

        // RDY-061 Phase 2: student advancement aggregate (per enrollment)
        opts.Projections.Snapshot<Cena.Actors.Advancement.StudentAdvancementState>(SnapshotLifecycle.Inline);

        // REV-014: Index SchoolId for efficient tenant-scoped queries
        opts.Schema.For<StudentProfileSnapshot>().Index(x => x.SchoolId);

        // STB-01: Active session tracking
        // FIND-data-004: Changed from Inline Snapshot to regular document.
        // This type is mutated directly via session.Store(), not rebuilt from events.
        opts.Schema.For<ActiveSessionSnapshot>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId);

        // STB-01c: Learning session queue projection for adaptive question selection
        // FIND-data-003: Changed from Inline Snapshot to regular document.
        // This type is mutated directly via session.Store() in SessionEndpoints,
        // not rebuilt from events via Apply handlers.
        opts.Schema.For<LearningSessionQueueProjection>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.SessionId)
            .Index(x => x.StartedAt);

        // FIND-data-009: Student lifetime stats projection for fast analytics
        // Replaces QueryAllRawEvents full-scans with single-document lookup
        opts.Projections.Add<StudentLifetimeStatsProjection>(ProjectionLifecycle.Inline);

        // FIND-arch-023: Session attempt history projection for fast session detail/replay
        // Replaces FetchStreamAsync event queries with single-document lookup
        opts.Projections.Add<SessionAttemptHistoryProjection>(ProjectionLifecycle.Inline);
        opts.Schema.For<SessionAttemptHistoryDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SessionId)
            .Index(x => x.StudentId);

        // FIND-arch-024: Feature flag projection for persistence and audit
        // Replaces in-memory storage with event-sourced persistence
        opts.Projections.Add<FeatureFlagProjection>(ProjectionLifecycle.Inline);
        opts.Schema.For<FeatureFlagDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Name);

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

        // ── Tenancy Documents (TENANCY-P1a) ──
        opts.Schema.For<InstituteDocument>()
            .Identity(x => x.Id)
            .Index(x => x.InstituteId)
            .Index(x => x.Type)
            .Index(x => x.Country);

        opts.Schema.For<CurriculumTrackDocument>()
            .Identity(x => x.Id)
            .Index(x => x.TrackId)
            .Index(x => x.Code)
            .Index(x => x.Subject)
            .Index(x => x.Status);

        // RDY-061 Phase 1: syllabus projection layer over the LO graph.
        // Defined in YAML (config/syllabi/*.yaml), ingested via the
        // DbAdmin `syllabus-ingest` command. Re-ingestion is idempotent.
        opts.Schema.For<SyllabusDocument>()
            .Identity(x => x.Id)
            .Index(x => x.TrackId)
            .Index(x => x.Track)
            .Index(x => x.Version);

        opts.Schema.For<ChapterDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SyllabusId)
            .Index(x => x.Order)
            .Index(x => x.Slug)
            .Index(x => x.MinistryCode);

        opts.Schema.For<EnrollmentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.EnrollmentId)
            .Index(x => x.StudentId)
            .Index(x => x.InstituteId)
            .Index(x => x.TrackId)
            .Index(x => x.Status);

        // ── Onboarding Self-Assessment (RDY-057) ──
        // Per-student affective snapshot captured during onboarding.
        // 90-day retention by default (reaper sweeps on ExpiresAt).
        // [MlExcluded] per ADR-0003; excluded from training corpora.
        opts.Schema.For<OnboardingSelfAssessmentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.CapturedAt)
            .Index(x => x.ExpiresAt!);

        // ── Stuck Diagnosis Document (RDY-063 Phase 1) ──
        // Session-scoped classifier output. 30-day retention per ADR-0003.
        // NO raw studentId — anon only. Indexed for item-quality queries
        // ("top N questions by encoding-stuck rate over last 7 days").
        opts.Schema.For<StuckDiagnosisDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SessionId)
            .Index(x => x.StudentAnonId)
            .Index(x => x.QuestionId)
            .Index(x => x.ChapterId!)
            .Index(x => x.Primary)
            .Index(x => x.Source)
            .Index(x => x.DayBucket)
            .Index(x => x.ExpiresAt);

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

        // ── Social Documents (STB-06b) ──
        opts.Schema.For<CommentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ItemId)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);

        opts.Schema.For<FriendRequestDocument>()
            .Identity(x => x.Id)
            .Index(x => x.FromStudentId)
            .Index(x => x.ToStudentId)
            .Index(x => x.Status);

        opts.Schema.For<FriendshipDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentAId)
            .Index(x => x.StudentBId);

        opts.Schema.For<StudyRoomDocument>()
            .Identity(x => x.Id)
            .Index(x => x.HostStudentId)
            .Index(x => x.IsPublic);

        opts.Schema.For<StudyRoomMembershipDocument>()
            .Identity(x => x.Id)
            .Index(x => x.RoomId)
            .Index(x => x.StudentId)
            .Index(x => x.IsActive);

        // ── Class Feed Documents (HARDEN SocialEndpoints) ──
        opts.Schema.For<ClassFeedItemDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ClassroomId)
            .Index(x => x.Kind)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);

        opts.Schema.For<PeerSolutionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.QuestionId)
            .Index(x => x.AuthorStudentId)
            .Index(x => x.PostedAt);

        // ── Question Document (HARDEN SessionEndpoints) ──
        opts.Schema.For<QuestionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.QuestionId)
            .Index(x => x.Subject)
            .Index(x => x.Difficulty)
            .Index(x => x.ConceptId)
            .Index(x => x.Grade)
            .Index(x => x.LearningObjectiveId!); // FIND-pedagogy-008

        // ── Learning Objective Document (FIND-pedagogy-008) ──
        opts.Schema.For<LearningObjectiveDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Code)
            .Index(x => x.Subject)
            .Index(x => x.IsActive);

        // ── Item Exposure Document (RDY-018: Sympson-Hetter exposure control) ──
        opts.Schema.For<Cena.Infrastructure.Assessment.ItemExposureDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ItemId)
            .Index(x => x.ObservedExposureRate)
            .Index(x => x.LastAdministeredAt);

        // ── Boss Attempt Document (STB-05b) ──
        opts.Schema.For<BossAttemptDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.BossBattleId)
            .Index(x => x.Date);

        // ── Challenge Catalog Documents (HARDEN STB-05) ──
        opts.Schema.For<DailyChallengeDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Date)
            .Index(x => x.Locale);

        opts.Schema.For<DailyChallengeCompletionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Date)
            .Index(x => x.Score);

        opts.Schema.For<CardChainDefinitionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ChainId)
            .Index(x => x.Subject);

        opts.Schema.For<CardChainProgressDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.ChainId);

        opts.Schema.For<TournamentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.IsActive)
            .Index(x => x.StartsAt)
            .Index(x => x.EndsAt);

        opts.Schema.For<TournamentRegistrationDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.TournamentId);

        // ── Content Catalog Documents (STB-08b) ──
        opts.Schema.For<ConceptDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ConceptId)
            .Index(x => x.Subject)
            .Index(x => x.Difficulty);

        opts.Schema.For<LearningPathDocument>()
            .Identity(x => x.Id)
            .Index(x => x.PathId)
            .Index(x => x.Subject)
            .Index(x => x.TargetGrade);

        // ── Analytics Projections (STB-09b) ──
        opts.Schema.For<StudentTimeBreakdown>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Date);

        opts.Schema.For<StudentFlowAccuracyProfile>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId);

        opts.Schema.For<SubjectMasteryTimeline>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Subject);

        // ── Notification Documents (STB-07b + STB-07c) ──
        opts.Schema.For<NotificationDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Read)
            .Index(x => x.CreatedAt);

        opts.Schema.For<NotificationPreferencesDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId);

        opts.Schema.For<WebPushSubscriptionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Endpoint);

        // ── Cultural Context Documents (ADM-012) ──
        opts.Schema.For<CulturalContextGroupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.Context);

        // RDY-056 §1.1: alias to short doc name — full type name "MethodologyEffectivenessByCultureDocument"
        // overflows Postgres NAMEDATALEN (63 chars) when combined with Marten's
        // `mt_doc_<alias>_idx_<column>` naming convention.
        opts.Schema.For<MethodologyEffectivenessByCultureDocument>()
            .DocumentAlias("method_effect_by_culture")
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.Methodology);

        opts.Schema.For<EquityAlertDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.Severity)
            .Index(x => x.DetectedAt);

        opts.Schema.For<ContentBalanceRecommendationDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.Language)
            .Index(x => x.Subject);

        // ── Platform Settings (ADM-008) — singleton doc, id='platform' ──
        opts.Schema.For<PlatformSettingsDocument>()
            .Identity(x => x.Id);

        // ── Focus Analytics Rollups (ADM-014) ──
        opts.Schema.For<FocusSessionRollupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.StudentId)
            .Index(x => x.Date);

        opts.Schema.For<ClassAttentionRollupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.ClassId)
            .Index(x => x.Date);

        opts.Schema.For<FocusDegradationRollupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId);

        // ── Mastery Rollups (ADM-016) ──
        opts.Schema.For<ClassMasteryRollupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.ClassId)
            .Index(x => x.Date);

        // Retired 2026-04-20 per prr-013 + ADR-0012 RDY-080. Use session-scoped
        // SessionRiskAssessment via LearningSessionActor. The CLR type lives
        // under Documents/Legacy/AtRiskStudentDocument.Legacy.cs marked
        // [Obsolete] — retained only so historical rows remain deserialisable
        // until we know no more orphans exist. No new writes; no new queries.

        opts.Schema.For<ConceptDifficultyDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.ConceptId);

        // ── Outreach Engagement (ADM-018) ──
        opts.Schema.For<OutreachEventDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.StudentId)
            .Index(x => x.Channel)
            .Index(x => x.SentAt);

        opts.Schema.For<OutreachBudgetDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId);

        opts.Schema.For<StudentNotificationPreferencesDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.SchoolId);

        // ── Ingestion Rollups (ADM-015) ──
        opts.Schema.For<IngestionMetricsRollupDocument>()
            .Identity(x => x.Id)
            .Index(x => x.SchoolId)
            .Index(x => x.Date);

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
        opts.Events.AddEventType<ConceptAttempted_V3>(); // TENANCY-P2a: V3 with EnrollmentId
        opts.Events.AddEventType<ConceptMastered_V1>();
        opts.Events.AddEventType<ConceptMastered_V2>(); // TENANCY-P2a: V2 with EnrollmentId
        opts.Events.AddEventType<MasterySeepageApplied_V1>(); // TENANCY-P2a: cross-track seepage
        opts.Events.AddEventType<MasteryDecayed_V1>();
        opts.Events.AddEventType<MethodologySwitched_V1>();
        opts.Events.AddEventType<StagnationDetected_V1>();
        opts.Events.AddEventType<AnnotationAdded_V1>();
        opts.Events.AddEventType<CognitiveLoadCooldownComplete_V1>();

        // STB-01: Session lifecycle events
        opts.Events.AddEventType<LearningSessionStarted_V1>();
        opts.Events.AddEventType<LearningSessionEnded_V1>();
        opts.Events.AddEventType<QuestionFallbackLanguage_V1>();
        opts.Events.AddEventType<OnboardingCompleted_V1>(); // STB-00
        opts.Events.AddEventType<AgeAndConsentRecorded_V1>(); // FIND-privacy-001

        // STB-05b: Challenge events
        opts.Events.AddEventType<ChallengeStarted_V1>();
        opts.Events.AddEventType<BossAttemptConsumed_V1>();
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
        // STB-01c: Session question answered event
        opts.Events.AddEventType<QuestionAnsweredInSession_V1>();
        // FIND-data-007b: Profile updated event for event-sourced profile changes
        opts.Events.AddEventType<ProfileUpdated_V1>();
        // FIND-data-009: Challenge completed event for StudentLifetimeStats
        opts.Events.AddEventType<ChallengeCompleted_V1>();
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

    private static void RegisterNotificationEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<NotificationDeleted_V1>();
        opts.Events.AddEventType<NotificationSnoozed_V1>();
        opts.Events.AddEventType<WebPushSubscribed_V1>();
        opts.Events.AddEventType<WebPushUnsubscribed_V1>();
    }

    /// <summary>
    /// FIND-data-012: Register messaging events for thread persistence.
    /// ThreadSummaryProjection maintains thread metadata in PostgreSQL.
    /// </summary>
    private static void RegisterMessagingEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<ThreadCreated_V1>();
        opts.Events.AddEventType<MessageSent_V1>();
        
        // Register ThreadSummaryProjection as inline for immediate consistency
        opts.Projections.Add<Cena.Actors.Messaging.ThreadSummaryProjection>(ProjectionLifecycle.Inline);
    }

    private static void RegisterFocusEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<FocusScoreUpdated_V1>();
        opts.Events.AddEventType<MindWanderingDetected_V1>();
        opts.Events.AddEventType<MicrobreakSuggested_V1>();
        opts.Events.AddEventType<MicrobreakTaken_V1>();
        opts.Events.AddEventType<MicrobreakSkipped_V1>();
    }

    /// <summary>
    /// TENANCY-P1c: Registers enrollment lifecycle events for multi-institute tenancy.
    /// All event type names use snake_case_v1 per FIND-data-005 convention.
    /// </summary>
    private static void RegisterEnrollmentEvents(StoreOptions opts)
    {
        opts.Events.MapEventType<InstituteCreated_V1>("institute_created_v1");
        opts.Events.MapEventType<CurriculumTrackPublished_V1>("curriculum_track_published_v1");
        opts.Events.MapEventType<ProgramCreated_V1>("program_created_v1");
        opts.Events.MapEventType<ProgramForkedFromPlatform_V1>("program_forked_from_platform_v1");
        opts.Events.MapEventType<ClassroomCreated_V1>("classroom_created_v1");
        opts.Events.MapEventType<ClassroomStatusChanged_V1>("classroom_status_changed_v1");
        opts.Events.MapEventType<EnrollmentCreated_V1>("enrollment_created_v1");
        opts.Events.MapEventType<EnrollmentStatusChanged_V1>("enrollment_status_changed_v1");
    }

    /// <summary>
    /// RDY-006 / ADR-0003: Register session-scoped misconception events.
    /// All three types carry [MlExcluded] and are filtered from exports/training.
    /// </summary>
    private static void RegisterMisconceptionEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<MisconceptionDetected_V1>();
        opts.Events.AddEventType<MisconceptionRemediated_V1>();
        opts.Events.AddEventType<SessionMisconceptionsScrubbed_V1>();
    }

    private static void RegisterQuestionEvents(StoreOptions opts)
    {
        opts.Events.AddEventType<QuestionAuthored_V1>();
        opts.Events.AddEventType<QuestionAuthored_V2>(); // FIND-pedagogy-008
        opts.Events.AddEventType<QuestionIngested_V1>();
        opts.Events.AddEventType<QuestionIngested_V2>(); // FIND-pedagogy-008
        opts.Events.AddEventType<QuestionAiGenerated_V1>();
        opts.Events.AddEventType<QuestionAiGenerated_V2>(); // FIND-pedagogy-008
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
        opts.Events.AddEventType<LearningObjectiveAssigned_V1>(); // FIND-pedagogy-008

        // RDY-061 Phase 2: syllabus advancement events.
        opts.Events.AddEventType<AdvancementStarted_V1>();
        opts.Events.AddEventType<ChapterUnlocked_V1>();
        opts.Events.AddEventType<ChapterStarted_V1>();
        opts.Events.AddEventType<ChapterMastered_V1>();
        opts.Events.AddEventType<ChapterDecayDetected_V1>();
        opts.Events.AddEventType<SpiralReviewCompleted_V1>();
        opts.Events.AddEventType<ChapterOverriddenByTeacher_V1>();
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

        // FIND-pedagogy-008: Question creation V1 -> V2 upcasters (add LearningObjectiveId).
        opts.RegisterUpcaster(QuestionAuthoredV1ToV2Upcaster.Instance);
        opts.RegisterUpcaster(QuestionIngestedV1ToV2Upcaster.Instance);
        opts.RegisterUpcaster(QuestionAiGeneratedV1ToV2Upcaster.Instance);

        // TENANCY-P2a: Enrollment-scoped mastery upcasters
        opts.RegisterUpcaster(ConceptAttemptedV2ToV3Upcaster.Instance);
        opts.RegisterUpcaster(ConceptMasteredV1ToV2Upcaster.Instance);
    }
}
