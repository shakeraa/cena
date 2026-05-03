// =============================================================================
// Cena Platform — Generated Bagrut paper-structure seeds (PRR-290)
//
// Closes the catalog gap from coordinator m_f578dc757ac8 item #2: the
// canonical hand-authored set in BagrutPaperStructureCatalog.cs covers
// 5 papers (806/default, 806/035582, 806/035581, 807/default, 036/default).
// Production needs 50+ structures across the major Bagrut math + physics
// שאלון codes spanning 2020–2025 × Moed A/B × Summer/Winter so the
// PRR-291 cohort path (frozen pool keyed on paperCode) has a realistic
// catalog to match against.
//
// Why generated, not hand-authored: 50+ hand-written PaperSection /
// PaperSlot blocks would push BagrutPaperStructureCatalog past the
// 500-LOC ratchet (ADR-0012) and bury real signal under boilerplate.
// The generator emits structures from a small (year, season, moed, exam,
// paperCode) tuple table, applies the canonical slot template for that
// exam (mirrored from the hand-authored "806/default" / "807/default" /
// "036/default" structures), and varies the per-section
// FallbackTopicId + per-slot Notes to capture per-paper emphasis drift.
//
// HONEST CAVEAT — these are synthetic structures. The שאלון codes
// follow the Ministry numbering convention (035xxx for math 5U, 036xxx
// for physics, 037xxx for math 4U) but are NOT mined from
// edu.gov.il. Real Ministry papers reach the catalog via the
// PRR-251 BagrutCorpus → OCR-cascade ingestion pipeline; this seeder
// produces dev-scaffold structures so the runner's slot-aware draw +
// PRR-291 cohort path have something to match against in dev/staging.
// Production overrides synthetic codes when real Ministry papers ingest
// (Marten upsert on the same composite Id).
// =============================================================================

namespace Cena.Actors.Assessment;

internal static class BagrutPaperStructureSeeds
{
    /// <summary>Year range covered by the synthetic seeds.</summary>
    public const int MinYear = 2020;
    public const int MaxYear = 2025;

    /// <summary>Number of structures the generator emits — ratcheted by
    /// the architecture test so a careless edit can't silently shrink the
    /// dev catalog. Update <see cref="BuildSyntheticStructures"/>'s tuple
    /// table to grow.</summary>
    public const int ExpectedCount = 72;

    /// <summary>
    /// Generate the synthetic dev catalog. Idempotent — every emitted
    /// structure has a stable composite Id keyed on (examCode, paperCode);
    /// re-running the seeder is a Marten upsert.
    /// </summary>
    public static IReadOnlyList<BagrutPaperStructureDocument> BuildSyntheticStructures()
    {
        var structures = new List<BagrutPaperStructureDocument>(capacity: ExpectedCount);

        for (int year = MinYear; year <= MaxYear; year++)
        {
            int yearOffset = (year - MinYear) * 10;   // 2020→0, 2021→10, …
            // Emit one paper per (exam, season, moed) tuple. Suffix layout:
            //   x00 = Summer Moed A, x01 = Summer Moed B,
            //   x02 = Winter Moed A, x03 = Winter Moed B
            // Examples (math 5U "035" prefix):
            //   2020 Summer Moed A → 035500
            //   2020 Winter Moed B → 035503
            //   2021 Summer Moed A → 035510 ...
            for (int sittingIdx = 0; sittingIdx < 4; sittingIdx++)
            {
                var (season, moed) = sittingIdx switch
                {
                    0 => ("Summer", "A"),
                    1 => ("Summer", "B"),
                    2 => ("Winter", "A"),
                    3 => ("Winter", "B"),
                    _ => throw new InvalidOperationException(),
                };
                int suffix = yearOffset + sittingIdx;

                // Math 5U variants → 035500..035553
                structures.Add(BuildMath5U(
                    paperCode: $"0355{suffix:D2}",
                    year: year, season: season, moed: moed));

                // Math 4U variants → 037500..037553
                structures.Add(BuildMath4U(
                    paperCode: $"0375{suffix:D2}",
                    year: year, season: season, moed: moed));

                // Physics 5U variants → 036500..036553
                structures.Add(BuildPhysics5U(
                    paperCode: $"0365{suffix:D2}",
                    year: year, season: season, moed: moed));
            }
        }

        return structures;
    }

    /// <summary>
    /// Math 5U (806) per-paper structure. Mirrors 806/default's section
    /// shape (5+4 choose 2) with the per-paper note carrying the year /
    /// season / moed for forensic traceability + slight slot-topic
    /// variation across the chunked-rotation table so different papers
    /// don't all draw from identical pools.
    /// </summary>
    private static BagrutPaperStructureDocument BuildMath5U(
        string paperCode, int year, string season, string moed)
    {
        var label = $"Math 5U {year} {season} Moed {moed}";
        // Rotation pattern: each paper rotates the Section A 4th-slot
        // topic across the math.functions / .vectors / .growthDecay
        // family so cohort-bound runs get different emphasis without
        // departing from the canonical 806 shape.
        var rotationIndex = ((year * 4) + season.GetHashCode() + moed.GetHashCode()) & 0x3;
        var sectionASlot4 = rotationIndex switch
        {
            0 => "math.functions",
            1 => "math.algebra.quadratics",
            2 => "math.geometry.plane",
            _ => "math.calculus.integral",
        };
        var sectionBSlot4 = rotationIndex switch
        {
            0 => "math.growthDecay",
            1 => "math.functions",
            2 => "math.calculus.integral",
            _ => "math.vectors",
        };

        return new BagrutPaperStructureDocument
        {
            Id = BagrutPaperStructureDocument.ComposeId("806", paperCode),
            ExamCode = "806",
            PaperCode = paperCode,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra",            2, 3, 14, $"Algebra · {label}"),
                        new(2, "math.trigonometry",       2, 3, 14, $"Trig · {label}"),
                        new(3, "math.calculus.derivative",2, 3, 14, $"Derivative · {label}"),
                        new(4, sectionASlot4,             2, 3, 14, $"Rotated · {label}"),
                        new(5, "math.geometry",           2, 3, 14, $"Geometry · {label}"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus.integral",  3, 4, 15, $"Integration · {label}"),
                        new(2, "math.probability",        3, 4, 15, $"Probability · {label}"),
                        new(3, "math.vectors",            3, 4, 15, $"Vectors · {label}"),
                        new(4, sectionBSlot4,             3, 4, 15, $"Rotated · {label}"),
                    }),
            },
        };
    }

    /// <summary>Math 4U (807) per-paper structure. Lighter calculus, same shape as 807/default.</summary>
    private static BagrutPaperStructureDocument BuildMath4U(
        string paperCode, int year, string season, string moed)
    {
        var label = $"Math 4U {year} {season} Moed {moed}";
        return new BagrutPaperStructureDocument
        {
            Id = BagrutPaperStructureDocument.ComposeId("807", paperCode),
            ExamCode = "807",
            PaperCode = paperCode,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 5,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.algebra",            1, 3, 14, $"Algebra · {label}"),
                        new(2, "math.trigonometry",       1, 3, 14, $"Trig · {label}"),
                        new(3, "math.calculus.derivative",2, 3, 14, $"Derivative · {label}"),
                        new(4, "math.functions",          1, 3, 14, $"Function analysis · {label}"),
                        new(5, "math.geometry",           1, 3, 14, $"Geometry · {label}"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 2,
                    FallbackTopicId: "math",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "math.calculus.derivative",2, 3, 15, $"Derivative app · {label}"),
                        new(2, "math.probability",        2, 3, 15, $"Probability · {label}"),
                        new(3, "math.functions",          2, 3, 15, $"Function model · {label}"),
                        new(4, "math.geometry",           2, 3, 15, $"Geometry proof · {label}"),
                    }),
            },
        };
    }

    /// <summary>Physics 5U (036) per-paper structure. 4+5 choose 3 mirroring 036/default.</summary>
    private static BagrutPaperStructureDocument BuildPhysics5U(
        string paperCode, int year, string season, string moed)
    {
        var label = $"Physics 5U {year} {season} Moed {moed}";
        var rotationIndex = ((year * 4) + season.GetHashCode() + moed.GetHashCode()) & 0x3;
        var sectionBSlot5 = rotationIndex switch
        {
            0 => "physics.thermodynamics",
            1 => "physics.modern",
            2 => "physics.electromagnetism",
            _ => "physics.optics",
        };

        return new BagrutPaperStructureDocument
        {
            Id = BagrutPaperStructureDocument.ComposeId("036", paperCode),
            ExamCode = "036",
            PaperCode = paperCode,
            TimeLimitMinutes = 180,
            Sections = new List<PaperSection>
            {
                new(
                    SectionLabel: "A",
                    RequiredAnswers: 4,
                    FallbackTopicId: "physics",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "physics.mechanics",     2, 3, 17, $"Mechanics · {label}"),
                        new(2, "physics.thermodynamics",2, 3, 17, $"Thermo · {label}"),
                        new(3, "physics.waves",         2, 3, 17, $"Waves · {label}"),
                        new(4, "physics.electricity",   2, 3, 17, $"Electricity · {label}"),
                    }),
                new(
                    SectionLabel: "B",
                    RequiredAnswers: 3,
                    FallbackTopicId: "physics",
                    Slots: new List<PaperSlot>
                    {
                        new(1, "physics.mechanics",      3, 4, 11, $"Mechanics long · {label}"),
                        new(2, "physics.electromagnetism",3, 4, 11, $"EM · {label}"),
                        new(3, "physics.optics",         3, 4, 11, $"Optics · {label}"),
                        new(4, "physics.modern",         3, 4, 11, $"Modern · {label}"),
                        new(5, sectionBSlot5,            3, 4, 11, $"Rotated · {label}"),
                    }),
            },
        };
    }
}
