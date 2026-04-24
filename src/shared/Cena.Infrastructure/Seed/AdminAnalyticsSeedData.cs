// =============================================================================
// Cena Platform -- Admin Analytics Seed Data (ADM-014/015/016/018)
// Idempotent seeders that populate realistic dev-tier rollups so admin
// dashboards work without running the full simulation pipeline.
// Every seeder guards on existence — safe to re-run on every startup.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

/// <summary>
/// Seeds rollup docs for focus analytics, ingestion metrics, mastery rollups,
/// and outreach events. Scoped to tenant "dev-school" (matches the Classroom
/// / User dev fixtures seeded elsewhere).
/// </summary>
public static class AdminAnalyticsSeedData
{
    private const string DevSchoolId = "dev-school";

    public static async Task SeedAllAsync(IDocumentStore store, ILogger logger)
    {
        await SeedFocusRollupsAsync(store, logger);
        await SeedClassMasteryRollupsAsync(store, logger);
        await SeedConceptDifficultyAsync(store, logger);
        await SeedOutreachEventsAsync(store, logger);
        await SeedOutreachBudgetAsync(store, logger);
        await SeedIngestionRollupsAsync(store, logger);
    }

    // ── Focus analytics rollups ──────────────────────────────────────────────

    public static async Task SeedFocusRollupsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var existing = await session.Query<FocusSessionRollupDocument>()
            .Where(x => x.SchoolId == DevSchoolId)
            .AnyAsync();
        if (existing)
        {
            logger.LogInformation("Focus rollups already seeded for {School}, skipping", DevSchoolId);
            return;
        }

        logger.LogInformation("Seeding focus session rollups for dev school...");
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);

        // Deterministic dev students so tests can look them up
        var students = new[]
        {
            ("stu-alice", "Alice Green", "class-grade-10a"),
            ("stu-bob", "Bob White", "class-grade-10a"),
            ("stu-carol", "Carol Black", "class-grade-10b"),
            ("stu-dan", "Dan Blue", "class-grade-10b"),
            ("stu-eli", "Eli Violet", "class-grade-11a"),
        };

        var rng = new Random(424242);
        int docs = 0;
        foreach (var (id, name, classId) in students)
        {
            for (int d = 29; d >= 0; d--)
            {
                var date = today.AddDays(-d);
                var baseScore = 60f + rng.NextSingle() * 35f;   // 60-95
                var mindWandering = rng.Next(0, 5);
                var taken = rng.Next(0, 4);
                var skipped = rng.Next(0, 2);

                var doc = new FocusSessionRollupDocument
                {
                    Id = $"{id}:{date:yyyy-MM-dd}",
                    StudentId = id,
                    StudentName = name,
                    ClassId = classId,
                    SchoolId = DevSchoolId,
                    Date = date,
                    AvgFocusScore = MathF.Round(baseScore, 1),
                    MinFocusScore = MathF.Round(baseScore - 10f, 1),
                    MaxFocusScore = MathF.Round(Math.Min(100f, baseScore + 10f), 1),
                    SessionCount = rng.Next(1, 4),
                    FocusMinutes = rng.Next(30, 180),
                    MindWanderingEvents = mindWandering,
                    MicrobreaksTaken = taken,
                    MicrobreaksSkipped = skipped,
                    MorningSessionCount = rng.Next(0, 2),
                    AfternoonSessionCount = rng.Next(0, 2),
                    EveningSessionCount = rng.Next(0, 2),
                };
                session.Store(doc);
                docs++;
            }
        }

        // Class attention rollups, one per class per day
        var classes = new[]
        {
            ("class-grade-10a", "Grade 10A"),
            ("class-grade-10b", "Grade 10B"),
            ("class-grade-11a", "Grade 11A"),
        };

        foreach (var (classId, className) in classes)
        {
            for (int d = 29; d >= 0; d--)
            {
                var date = today.AddDays(-d);
                var hourlySlots = Enumerable.Range(8, 9).Select(h => new ClassAttentionHourSlot
                {
                    Hour = h,
                    DayOfWeek = date.DayOfWeek.ToString().Substring(0, 3),
                    AvgFocusScore = MathF.Round(50f + rng.NextSingle() * 40f, 1),
                    SampleSize = rng.Next(10, 25),
                }).ToList();

                var subjectSlots = new List<ClassAttentionSubjectSlot>
                {
                    new() { Subject = "Math", AvgFocusScore = MathF.Round(60f + rng.NextSingle() * 30f, 1), SessionCount = rng.Next(10, 40) },
                    new() { Subject = "Physics", AvgFocusScore = MathF.Round(55f + rng.NextSingle() * 30f, 1), SessionCount = rng.Next(10, 30) },
                    new() { Subject = "Chemistry", AvgFocusScore = MathF.Round(55f + rng.NextSingle() * 30f, 1), SessionCount = rng.Next(8, 25) },
                };

                var rollup = new ClassAttentionRollupDocument
                {
                    Id = $"{classId}:{date:yyyy-MM-dd}",
                    ClassId = classId,
                    ClassName = className,
                    SchoolId = DevSchoolId,
                    Date = date,
                    AvgAttentionScore = MathF.Round(60f + rng.NextSingle() * 25f, 1),
                    TotalStudents = rng.Next(18, 25),
                    AtRiskStudentCount = rng.Next(0, 5),
                    HourlyAttention = hourlySlots,
                    SubjectAttention = subjectSlots,
                };
                session.Store(rollup);
                docs++;
            }
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} focus/class attention rollup docs", docs);
    }

    // ── Mastery class rollups + at-risk ──────────────────────────────────────

    public static async Task SeedClassMasteryRollupsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var existing = await session.Query<ClassMasteryRollupDocument>()
            .Where(x => x.SchoolId == DevSchoolId)
            .AnyAsync();
        if (existing)
        {
            logger.LogInformation("Class mastery rollups already seeded, skipping");
            return;
        }

        logger.LogInformation("Seeding class mastery rollups for dev school...");
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var rng = new Random(131313);
        int docs = 0;

        var classes = new[]
        {
            ("class-grade-10a", "Grade 10A"),
            ("class-grade-10b", "Grade 10B"),
            ("class-grade-11a", "Grade 11A"),
        };

        foreach (var (classId, className) in classes)
        {
            for (int d = 29; d >= 0; d--)
            {
                var date = today.AddDays(-d);
                var beginner = rng.Next(2, 6);
                var developing = rng.Next(4, 9);
                var proficient = rng.Next(6, 12);
                var master = rng.Next(2, 6);
                var total = beginner + developing + proficient + master;
                var avgMastery = (beginner * 0.2f + developing * 0.45f + proficient * 0.7f + master * 0.9f) / total;

                var subjectBreakdown = new List<ClassMasterySubjectSlot>
                {
                    new() { Subject = "Math", AvgMasteryLevel = 0.55f + rng.NextSingle() * 0.3f, ConceptCount = 42, MasteredCount = rng.Next(10, 30) },
                    new() { Subject = "Physics", AvgMasteryLevel = 0.5f + rng.NextSingle() * 0.3f, ConceptCount = 35, MasteredCount = rng.Next(8, 25) },
                    new() { Subject = "Chemistry", AvgMasteryLevel = 0.45f + rng.NextSingle() * 0.3f, ConceptCount = 32, MasteredCount = rng.Next(6, 22) },
                    new() { Subject = "Biology", AvgMasteryLevel = 0.5f + rng.NextSingle() * 0.3f, ConceptCount = 35, MasteredCount = rng.Next(6, 22) },
                };

                var rollup = new ClassMasteryRollupDocument
                {
                    Id = $"{classId}:{date:yyyy-MM-dd}",
                    ClassId = classId,
                    ClassName = className,
                    SchoolId = DevSchoolId,
                    Date = date,
                    AvgMastery = MathF.Round(avgMastery, 3),
                    TotalStudents = total,
                    BeginnerCount = beginner,
                    DevelopingCount = developing,
                    ProficientCount = proficient,
                    MasterCount = master,
                    // prr-013 retirement 2026-04-20: AtRiskCount removed.
                    // "At-risk" is session-scoped via SessionRiskAssessment,
                    // never aggregated onto a persisted rollup (ADR-0003 + RDY-080).
                    LearningVelocity = 2f + rng.NextSingle() * 1.5f,
                    LearningVelocityChange = rng.NextSingle() * 0.4f - 0.2f,
                    SubjectBreakdown = subjectBreakdown,
                };
                session.Store(rollup);
                docs++;
            }
        }

        // prr-013 retirement 2026-04-20: the AtRiskStudentDocument seeder was
        // removed. Teacher-facing "students needing intervention" data is now
        // session-scoped via SessionRiskAssessment inside the session actor
        // (ADR-0003 + RDY-080). No dev-tier seeding is required for that seam
        // — it is computed live during an active session and never persisted.

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} class mastery rollup docs", docs);
    }

    // ── Concept difficulty per school ────────────────────────────────────────

    public static async Task SeedConceptDifficultyAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var existing = await session.Query<ConceptDifficultyDocument>()
            .Where(x => x.SchoolId == DevSchoolId)
            .AnyAsync();
        if (existing)
        {
            logger.LogInformation("Concept difficulty docs already seeded, skipping");
            return;
        }

        logger.LogInformation("Seeding concept difficulty for dev school...");
        var rng = new Random(909090);
        // Real bagrut concept slice
        var concepts = new[]
        {
            ("M-ALG-04", "Quadratic Equations", "Math"),
            ("M-CAL-01", "Limits", "Math"),
            ("M-CAL-02", "Derivative Definition", "Math"),
            ("M-CAL-03", "Derivative Rules", "Math"),
            ("M-VEC-01", "Vector Basics", "Math"),
            ("P-KIN-01", "Displacement & Velocity", "Physics"),
            ("P-KIN-03", "Projectile Motion", "Physics"),
            ("P-DYN-01", "Newton's Laws", "Physics"),
            ("P-ENE-01", "Work & Kinetic Energy", "Physics"),
            ("C-STO-01", "Moles & Molar Mass", "Chemistry"),
            ("C-EQU-01", "Chemical Equilibrium", "Chemistry"),
            ("C-ACB-01", "Acids, Bases & pH", "Chemistry"),
            ("B-CEL-01", "Cell Structure", "Biology"),
            ("B-GEN-01", "Mendelian Genetics", "Biology"),
            ("B-MOL-01", "DNA Structure & Replication", "Biology"),
        };

        int docs = 0;
        foreach (var (cid, name, subject) in concepts)
        {
            var attempts = rng.Next(200, 800);
            var correct = (int)(attempts * (0.4 + rng.NextSingle() * 0.4));
            var avgMastery = correct / (float)attempts;

            var doc = new ConceptDifficultyDocument
            {
                Id = $"{DevSchoolId}:{cid}",
                SchoolId = DevSchoolId,
                ConceptId = cid,
                ConceptName = name,
                Subject = subject,
                TotalAttempts = attempts,
                CorrectAttempts = correct,
                AvgMastery = MathF.Round(avgMastery, 3),
                StruggleRate = MathF.Round(1f - avgMastery, 3),
                StudentSampleSize = rng.Next(15, 40),
            };
            session.Store(doc);
            docs++;
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} concept difficulty docs", docs);
    }

    // ── Outreach events ──────────────────────────────────────────────────────

    public static async Task SeedOutreachEventsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var existing = await session.Query<OutreachEventDocument>()
            .Where(x => x.SchoolId == DevSchoolId)
            .AnyAsync();
        if (existing)
        {
            logger.LogInformation("Outreach events already seeded, skipping");
            return;
        }

        logger.LogInformation("Seeding outreach events for dev school...");
        var rng = new Random(777);
        var students = new[]
        {
            ("stu-alice", "class-grade-10a"),
            ("stu-bob", "class-grade-10a"),
            ("stu-carol", "class-grade-10b"),
            ("stu-dan", "class-grade-10b"),
            ("stu-eli", "class-grade-11a"),
        };
        var channels = new[] { "WhatsApp", "Telegram", "Push", "Voice" };
        var triggers = new[] { "disengagement", "stagnation", "reminder", "onboarding" };
        var previews = new[]
        {
            "We noticed you haven't logged in for a few days — ready to continue?",
            "Your mastery on quadratics needs a quick review. 5 min?",
            "Nice streak last week! Keep it going today.",
            "Your learning plan for this week is ready.",
        };

        int docs = 0;
        for (int i = 0; i < 200; i++)
        {
            var student = students[rng.Next(students.Length)];
            var channel = channels[rng.Next(channels.Length)];
            var trigger = triggers[rng.Next(triggers.Length)];
            var preview = previews[rng.Next(previews.Length)];
            var sentAt = DateTimeOffset.UtcNow.AddDays(-rng.Next(0, 60)).AddHours(-rng.Next(0, 24));
            var delivered = rng.NextSingle() > 0.08f;
            var opened = delivered && rng.NextSingle() > 0.45f;
            var clicked = opened && rng.NextSingle() > 0.5f;
            var optedOut = !delivered && rng.NextSingle() > 0.9f;
            var wasMerged = rng.NextSingle() > 0.6f;

            var evt = new OutreachEventDocument
            {
                Id = $"evt-{i:D4}-{Guid.NewGuid():N}".Substring(0, 32),
                StudentId = student.Item1,
                SchoolId = DevSchoolId,
                ClassId = student.Item2,
                Channel = channel,
                TriggerReason = trigger,
                MessagePreview = preview,
                SentAt = sentAt,
                Delivered = delivered,
                Opened = opened,
                Clicked = clicked,
                OptedOut = optedOut,
                WasMerged = wasMerged,
                DeliveredAt = delivered ? sentAt.AddSeconds(rng.Next(1, 30)) : null,
                OpenedAt = opened ? sentAt.AddMinutes(rng.Next(1, 120)) : null,
                ReEngagedAt = clicked ? sentAt.AddHours(rng.Next(1, 24)) : null,
            };
            session.Store(evt);
            docs++;
        }

        // Notification preferences per student
        foreach (var (sid, _) in students)
        {
            var prefs = new StudentNotificationPreferencesDocument
            {
                Id = $"notif-prefs:{sid}",
                StudentId = sid,
                SchoolId = DevSchoolId,
                WhatsAppEnabled = true,
                TelegramEnabled = rng.NextSingle() > 0.5f,
                PushEnabled = true,
                VoiceEnabled = rng.NextSingle() > 0.8f,
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            };
            session.Store(prefs);
            docs++;
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} outreach events + prefs", docs);
    }

    public static async Task SeedOutreachBudgetAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var id = $"outreach-budget:{DevSchoolId}";
        var existing = await session.LoadAsync<OutreachBudgetDocument>(id);
        if (existing is not null)
        {
            logger.LogInformation("Outreach budget already seeded, skipping");
            return;
        }

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var doc = new OutreachBudgetDocument
        {
            Id = id,
            SchoolId = DevSchoolId,
            DailyBudget = 1000,
            UsedToday = 180,
            BudgetDate = today,
        };
        session.Store(doc);
        await session.SaveChangesAsync();
        logger.LogInformation("Seeded outreach budget for {School}", DevSchoolId);
    }

    // ── Ingestion rollups ────────────────────────────────────────────────────

    public static async Task SeedIngestionRollupsAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();
        var existing = await session.Query<IngestionMetricsRollupDocument>()
            .Where(x => x.SchoolId == DevSchoolId)
            .AnyAsync();
        if (existing)
        {
            logger.LogInformation("Ingestion rollups already seeded, skipping");
            return;
        }

        logger.LogInformation("Seeding ingestion rollups for dev school...");
        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var rng = new Random(6543);
        int docs = 0;

        var stageNames = new[] { "Incoming", "OcrProcessing", "Segmented", "Normalized", "Classified", "Deduplicated", "ReCreated", "InReview", "Published" };

        for (int d = 6; d >= 0; d--)
        {
            var date = today.AddDays(-d);
            var total = rng.Next(8, 30);
            var failed = rng.Next(0, total / 5 + 1);
            var succeeded = rng.Next(total / 2, total - failed);
            var inProgress = total - succeeded - failed;

            var stageCounts = stageNames.Select(s => new IngestionStageCount
            {
                Stage = s,
                Count = rng.Next(0, total + 1),
            }).ToList();

            var stageFailures = stageNames.Select(s =>
            {
                var sTotal = rng.Next(0, 20);
                var sFailed = sTotal > 0 ? rng.Next(0, sTotal / 3 + 1) : 0;
                return new IngestionStageFailureRate
                {
                    Stage = s,
                    Total = sTotal,
                    Failed = sFailed,
                    Rate = sTotal > 0 ? (float)sFailed / sTotal : 0f,
                };
            }).ToList();

            var doc = new IngestionMetricsRollupDocument
            {
                Id = $"{DevSchoolId}:{date:yyyy-MM-dd}",
                SchoolId = DevSchoolId,
                Date = date,
                TotalJobs = total,
                SucceededJobs = succeeded,
                FailedJobs = failed,
                InProgressJobs = Math.Max(0, inProgress),
                QuestionsExtracted = rng.Next(50, 300),
                StageCounts = stageCounts,
                StageFailureRates = stageFailures,
            };
            session.Store(doc);
            docs++;
        }

        await session.SaveChangesAsync();
        logger.LogInformation("Seeded {Count} ingestion rollup docs", docs);
    }
}
