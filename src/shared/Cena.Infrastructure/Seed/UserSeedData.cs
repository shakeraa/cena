// =============================================================================
// Cena Platform -- Demo User Seed Data
// Seeds realistic users across all 6 roles for development/demo.
// Israeli school context: Hebrew + Arabic names, school IDs, grades.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Seed;

public static class UserSeedData
{
    private static readonly AdminUser[] DemoUsers =
    [
        // ── SUPER_ADMIN ──
        new()
        {
            Id = "sa-001", Email = "shaker.abuayoub@gmail.com",
            FullName = "Shaker Abu Ayoub", Role = CenaRole.SUPER_ADMIN,
            Status = UserStatus.Active, Locale = "en",
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z")
        },

        // ── ADMINS ──
        new()
        {
            Id = "adm-001", Email = "admin@cena-demo.edu",
            FullName = "רונית כהן", Role = CenaRole.ADMIN,
            Status = UserStatus.Active, School = "school-haifa-01",
            Locale = "he", CreatedAt = DateTimeOffset.Parse("2026-01-15T00:00:00Z")
        },
        new()
        {
            Id = "adm-002", Email = "admin2@cena-demo.edu",
            FullName = "سمير حداد", Role = CenaRole.ADMIN,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Locale = "ar", CreatedAt = DateTimeOffset.Parse("2026-01-15T00:00:00Z")
        },

        // ── MODERATORS ──
        new()
        {
            Id = "mod-001", Email = "moderator@cena-demo.edu",
            FullName = "יעל לוי", Role = CenaRole.MODERATOR,
            Status = UserStatus.Active, School = "school-haifa-01",
            Locale = "he", CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },

        // ── TEACHERS ──
        new()
        {
            Id = "tch-001", Email = "teacher.math@cena-demo.edu",
            FullName = "דר' מיכל אברהם", Role = CenaRole.TEACHER,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },
        new()
        {
            Id = "tch-002", Email = "teacher.math2@cena-demo.edu",
            FullName = "أحمد خطيب", Role = CenaRole.TEACHER,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },
        new()
        {
            Id = "tch-003", Email = "teacher.physics@cena-demo.edu",
            FullName = "עמית גולן", Role = CenaRole.TEACHER,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "11", Locale = "he",
            CreatedAt = DateTimeOffset.Parse("2026-02-10T00:00:00Z")
        },

        // ── PARENTS ──
        new()
        {
            Id = "par-001", Email = "parent.cohen@cena-demo.edu",
            FullName = "אבי כהן", Role = CenaRole.PARENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Locale = "he", CreatedAt = DateTimeOffset.Parse("2026-02-15T00:00:00Z")
        },
        new()
        {
            Id = "par-002", Email = "parent.haddad@cena-demo.edu",
            FullName = "فاطمة حداد", Role = CenaRole.PARENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Locale = "ar", CreatedAt = DateTimeOffset.Parse("2026-02-15T00:00:00Z")
        },

        // ── STUDENTS (matching MasterySimulator archetypes) ──

        // High Achievers (top 15%)
        new()
        {
            Id = "stu-001", Email = "noa.levi@cena-demo.edu",
            FullName = "נועה לוי", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-27T08:30:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },
        new()
        {
            Id = "stu-002", Email = "omar.mansour@cena-demo.edu",
            FullName = "عمر منصور", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-27T09:15:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },

        // Steady Learners (middle 40%)
        new()
        {
            Id = "stu-003", Email = "yael.mizrachi@cena-demo.edu",
            FullName = "יעל מזרחי", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-26T14:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-05T00:00:00Z")
        },
        new()
        {
            Id = "stu-004", Email = "layla.khatib@cena-demo.edu",
            FullName = "ليلى خطيب", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-26T16:30:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-05T00:00:00Z")
        },
        new()
        {
            Id = "stu-005", Email = "daniel.shapiro@cena-demo.edu",
            FullName = "דניאל שפירא", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-25T10:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-05T00:00:00Z")
        },
        new()
        {
            Id = "stu-006", Email = "rami.haddad@cena-demo.edu",
            FullName = "رامي حداد", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-27T07:45:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-05T00:00:00Z")
        },

        // Struggling students (bottom 20%)
        new()
        {
            Id = "stu-007", Email = "tal.ben-david@cena-demo.edu",
            FullName = "טל בן דוד", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-22T11:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-10T00:00:00Z")
        },
        new()
        {
            Id = "stu-008", Email = "maya.saleh@cena-demo.edu",
            FullName = "مايا صالح", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-20T15:30:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-10T00:00:00Z")
        },

        // Fast & Careless
        new()
        {
            Id = "stu-009", Email = "ido.peretz@cena-demo.edu",
            FullName = "עידו פרץ", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-27T07:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-10T00:00:00Z")
        },

        // Slow & Thorough
        new()
        {
            Id = "stu-010", Email = "hana.daoud@cena-demo.edu",
            FullName = "هناء داود", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-26T18:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-10T00:00:00Z")
        },

        // Inconsistent
        new()
        {
            Id = "stu-011", Email = "roi.azulay@cena-demo.edu",
            FullName = "רועי אזולאי", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            LastLoginAt = DateTimeOffset.Parse("2026-03-18T09:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-15T00:00:00Z")
        },
        new()
        {
            Id = "stu-012", Email = "nour.ahmad@cena-demo.edu",
            FullName = "نور أحمد", Role = CenaRole.STUDENT,
            Status = UserStatus.Active, School = "school-nazareth-01",
            Grade = "10", Locale = "ar",
            LastLoginAt = DateTimeOffset.Parse("2026-03-15T12:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-15T00:00:00Z")
        },

        // ── Suspended student (for admin UI testing) ──
        new()
        {
            Id = "stu-013", Email = "suspended@cena-demo.edu",
            FullName = "תלמיד מושהה", Role = CenaRole.STUDENT,
            Status = UserStatus.Suspended, School = "school-haifa-01",
            Grade = "9", Locale = "he",
            SuspensionReason = "Academic integrity violation — repeated answer copying detected",
            SuspendedAt = DateTimeOffset.Parse("2026-03-20T10:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z")
        },

        // ── Pending invite (for admin UI testing) ──
        new()
        {
            Id = "stu-014", Email = "pending.invite@cena-demo.edu",
            FullName = "Pending Student", Role = CenaRole.STUDENT,
            Status = UserStatus.Pending, School = "school-haifa-01",
            Grade = "9", Locale = "en",
            CreatedAt = DateTimeOffset.Parse("2026-03-25T00:00:00Z")
        },
    ];

    /// <summary>
    /// Seeds demo users into Marten. Upserts — safe to re-run.
    /// Call from Program.cs startup alongside RoleSeedData.SeedRolesAsync.
    /// </summary>
    public static async Task SeedUsersAsync(IDocumentStore store, ILogger logger)
    {
        await using var session = store.LightweightSession();

        int created = 0;
        int updated = 0;

        foreach (var user in DemoUsers)
        {
            var existing = await session.LoadAsync<AdminUser>(user.Id);
            if (existing == null)
            {
                session.Store(user);
                created++;
            }
            else
            {
                // Update fields that may have changed, preserve runtime state
                session.Store(user with
                {
                    LastLoginAt = existing.LastLoginAt ?? user.LastLoginAt,
                    SuspensionReason = existing.SuspensionReason ?? user.SuspensionReason,
                    SuspendedAt = existing.SuspendedAt ?? user.SuspendedAt,
                    SoftDeleted = existing.SoftDeleted
                });
                updated++;
            }
        }

        await session.SaveChangesAsync();

        logger.LogInformation(
            "User seed complete: {Created} created, {Updated} updated, {Total} total demo users",
            created, updated, DemoUsers.Length);
    }

    // ── Hebrew/Arabic name pools for simulated student generation ──
    private static readonly string[] HebrewFirstNames =
        ["נועה", "יעל", "שירה", "תמר", "מיכל", "דניאל", "עידו", "רועי", "אלון", "גיל",
         "אורי", "ליאור", "עומר", "איתי", "נדב", "טל", "רון", "דור", "שי", "אריאל",
         "הילה", "מאיה", "רותם", "ענבר", "אביב", "עדי", "ליבי", "נועם", "יונתן", "אמיר"];

    private static readonly string[] HebrewLastNames =
        ["כהן", "לוי", "מזרחי", "פרץ", "ביטון", "אזולאי", "שפירא", "אברהם", "דוד", "גולן",
         "בן דוד", "אלון", "רוזן", "ברק", "שמעון", "חיים", "יוסף", "מלכה", "עמר", "סויסה"];

    private static readonly string[] ArabicFirstNames =
        ["عمر", "ليلى", "رامي", "نور", "مايا", "أحمد", "فاطمة", "محمد", "سارة", "يوسف",
         "هناء", "خالد", "ريم", "طارق", "دانا", "سامي", "لينا", "عادل", "جنى", "كريم",
         "ياسمين", "بشار", "رنا", "وليد", "آية", "حسام", "ميار", "فادي", "سلمى", "زيد"];

    private static readonly string[] ArabicLastNames =
        ["حداد", "خطيب", "منصور", "صالح", "داود", "أحمد", "عيسى", "نصار", "جبران", "سعيد",
         "شحادة", "عودة", "بكري", "زعبي", "طوقان", "كنعان", "مصري", "حسين", "عمري", "قاسم"];

    /// <summary>
    /// Generate AdminUser records for simulated students from GenerateRealisticCohort.
    /// Each simulated student gets a matching AdminUser in Marten for the admin dashboard.
    /// </summary>
    public static async Task SeedSimulatedStudentsAsync(
        IDocumentStore store, ILogger logger, int totalStudents = 100, int seed = 42)
    {
        await using var session = store.LightweightSession();
        var rng = new Random(seed);

        // Distribution matching MasterySimulator.GenerateRealisticCohort
        var distribution = new (string Archetype, double Pct, string Tag)[]
        {
            ("genius",           0.05, "Genius"),
            ("highachiever",     0.10, "HighAchiever"),
            ("steadylearner",    0.30, "SteadyLearner"),
            ("struggling",       0.15, "Struggling"),
            ("fastcareless",     0.10, "FastCareless"),
            ("slowthorough",     0.10, "SlowThorough"),
            ("inconsistent",     0.10, "Inconsistent"),
            ("verylowcognitive", 0.10, "VeryLowCognitive"),
        };

        int studentIndex = 0;
        int created = 0;
        var startDate = DateTimeOffset.Parse("2026-01-20T00:00:00Z");

        foreach (var (archetype, pct, tag) in distribution)
        {
            int count = (int)Math.Round(totalStudents * pct);

            for (int i = 0; i < count; i++)
            {
                studentIndex++;
                var id = $"sim-{archetype}-{studentIndex:D3}";

                var existing = await session.LoadAsync<AdminUser>(id);
                if (existing != null) continue; // Already seeded

                // Alternate schools and locales
                bool isHebrew = rng.NextDouble() < 0.55; // 55% Hebrew, 45% Arabic (Israeli demographics)
                var school = isHebrew ? "school-haifa-01" : "school-nazareth-01";
                var locale = isHebrew ? "he" : "ar";
                var grade = rng.Next(9, 12).ToString(); // Grades 9-11

                string fullName;
                string email;
                if (isHebrew)
                {
                    var first = HebrewFirstNames[rng.Next(HebrewFirstNames.Length)];
                    var last = HebrewLastNames[rng.Next(HebrewLastNames.Length)];
                    fullName = $"{first} {last}";
                    email = $"sim.{studentIndex:D3}@cena-demo.edu";
                }
                else
                {
                    var first = ArabicFirstNames[rng.Next(ArabicFirstNames.Length)];
                    var last = ArabicLastNames[rng.Next(ArabicLastNames.Length)];
                    fullName = $"{first} {last}";
                    email = $"sim.{studentIndex:D3}@cena-demo.edu";
                }

                // Last login varies by archetype engagement level
                var daysAgo = tag switch
                {
                    "Genius" => rng.Next(0, 2),
                    "HighAchiever" => rng.Next(0, 3),
                    "SteadyLearner" => rng.Next(0, 5),
                    "FastCareless" => rng.Next(0, 4),
                    "SlowThorough" => rng.Next(0, 3),
                    "Inconsistent" => rng.Next(3, 14),
                    "Struggling" => rng.Next(2, 10),
                    "VeryLowCognitive" => rng.Next(5, 21),
                    _ => rng.Next(0, 7)
                };

                session.Store(new AdminUser
                {
                    Id = id,
                    Email = email,
                    FullName = fullName,
                    Role = CenaRole.STUDENT,
                    Status = UserStatus.Active,
                    School = school,
                    Grade = grade,
                    Locale = locale,
                    LastLoginAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
                    CreatedAt = startDate.AddDays(rng.Next(0, 14))
                });
                created++;
            }
        }

        await session.SaveChangesAsync();

        logger.LogInformation(
            "Simulated student seed: {Created} students created ({Total} target, 8 archetypes, 2-month history)",
            created, totalStudents);
    }
}
