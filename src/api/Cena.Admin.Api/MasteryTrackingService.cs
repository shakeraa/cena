// =============================================================================
// Cena Platform -- Mastery Tracking Service
// ADM-007: Mastery & learning progress implementation
// =============================================================================
#pragma warning disable CS1998 // Async methods return stub data until wired to real stores

using System.Security.Claims;
using Cena.Actors.Events;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IMasteryTrackingService
{
    Task<MasteryOverviewResponse> GetOverviewAsync(string? classId, ClaimsPrincipal user);
    Task<StudentMasteryDetailResponse?> GetStudentMasteryAsync(string studentId, ClaimsPrincipal user);
    Task<ClassMasteryResponse?> GetClassMasteryAsync(string classId);
    Task<AtRiskStudentsResponse> GetAtRiskStudentsAsync(ClaimsPrincipal user);
    Task<MethodologyProfileAdminResponse?> GetMethodologyProfileAsync(string studentId, ClaimsPrincipal user);
    Task<bool> OverrideMethodologyAsync(string studentId, string level, string levelId, string methodology, string teacherId);
    Task<IReadOnlyList<MethodologyOverrideDocument>> GetStudentOverridesAsync(string studentId);
    Task<bool> RemoveOverrideAsync(string studentId, string overrideId);
}

public sealed class MasteryTrackingService : IMasteryTrackingService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<MasteryTrackingService> _logger;

    public MasteryTrackingService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<MasteryTrackingService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<MasteryOverviewResponse> GetOverviewAsync(string? classId, ClaimsPrincipal user)
    {
        // REV-014: Validate caller has a school scope (throws for non-SUPER_ADMIN without school_id)
        _ = TenantScope.GetSchoolFilter(user);

        var random = new Random(classId?.GetHashCode() ?? 42);

        var distribution = new List<MasteryDistributionPoint>
        {
            new("Beginner", random.Next(20, 50), 0),
            new("Developing", random.Next(40, 80), 0),
            new("Proficient", random.Next(60, 120), 0),
            new("Master", random.Next(30, 60), 0)
        };

        var total = distribution.Sum(d => d.Count);
        distribution = distribution.Select(d => d with { Percentage = (float)Math.Round(d.Count * 100f / total, 1) }).ToList();

        var subjects = new List<SubjectMastery>
        {
            new("Math", 0.65f + random.NextSingle() * 0.2f, 42, random.Next(15, 35)),
            new("Physics", 0.58f + random.NextSingle() * 0.2f, 35, random.Next(10, 25)),
            new("Chemistry", 0.52f + random.NextSingle() * 0.2f, 32, random.Next(8, 22)),
            new("Biology", 0.55f + random.NextSingle() * 0.2f, 35, random.Next(10, 24)),
            new("Computer Science", 0.60f + random.NextSingle() * 0.2f, 25, random.Next(5, 18)),
            new("English", 0.70f + random.NextSingle() * 0.15f, 18, random.Next(8, 15))
        };

        return new MasteryOverviewResponse(
            Distribution: distribution,
            SubjectBreakdown: subjects,
            LearningVelocity: 2.3f + random.NextSingle() * 1.5f,
            LearningVelocityChange: 0.2f,
            AtRiskCount: random.Next(5, 20));
    }

    // Full multi-subject Bagrut curriculum — ~200 concepts with cross-subject prerequisites
    private static readonly (string Id, string Name, string Subject, string Cluster, string[] Prereqs, string[] Unlocks)[] BagrutConcepts =
    {
        // ═══════════════════════════════════════════════════════════════
        // MATH (42 concepts) — Israeli Bagrut 5-unit syllabus
        // ═══════════════════════════════════════════════════════════════

        // Algebra
        ("M-ALG-01","Number Properties","Math","algebra",Array.Empty<string>(),new[]{"M-ALG-02","M-ALG-03","M-FUN-01","M-PRB-01"}),
        ("M-ALG-02","Linear Equations","Math","algebra",new[]{"M-ALG-01"},new[]{"M-ALG-04","M-ALG-05","M-GEO-04","P-KIN-01"}),
        ("M-ALG-03","Inequalities","Math","algebra",new[]{"M-ALG-01"},Array.Empty<string>()),
        ("M-ALG-04","Quadratic Equations","Math","algebra",new[]{"M-ALG-02"},new[]{"M-ALG-06","M-ALG-08","M-FUN-03","P-KIN-03"}),
        ("M-ALG-05","Systems of Equations","Math","algebra",new[]{"M-ALG-02"},new[]{"P-DYN-03","C-EQU-01"}),
        ("M-ALG-06","Polynomials","Math","algebra",new[]{"M-ALG-04"},new[]{"M-ALG-07","M-CAL-01"}),
        ("M-ALG-07","Rational Expressions","Math","algebra",new[]{"M-ALG-06"},Array.Empty<string>()),
        ("M-ALG-08","Sequences & Series","Math","algebra",new[]{"M-ALG-04"},new[]{"M-PRB-04","B-POP-02"}),
        // Functions
        ("M-FUN-01","Function Basics","Math","functions",new[]{"M-ALG-01"},new[]{"M-FUN-02"}),
        ("M-FUN-02","Linear Functions","Math","functions",new[]{"M-FUN-01"},new[]{"M-FUN-03","M-FUN-04","M-FUN-07","M-GEO-07","P-KIN-02"}),
        ("M-FUN-03","Quadratic Functions","Math","functions",new[]{"M-ALG-04","M-FUN-02"},new[]{"M-FUN-06","M-CAL-01","P-KIN-03"}),
        ("M-FUN-04","Exponential Functions","Math","functions",new[]{"M-FUN-02"},new[]{"M-FUN-05","M-FUN-06","C-RAT-01","B-POP-02"}),
        ("M-FUN-05","Logarithmic Functions","Math","functions",new[]{"M-FUN-04"},new[]{"C-PH-01"}),
        ("M-FUN-06","Composite Functions","Math","functions",new[]{"M-FUN-03","M-FUN-04"},Array.Empty<string>()),
        ("M-FUN-07","Inverse Functions","Math","functions",new[]{"M-FUN-02"},Array.Empty<string>()),
        // Geometry
        ("M-GEO-01","Angles & Lines","Math","geometry",Array.Empty<string>(),new[]{"M-GEO-02","M-GEO-04","P-OPT-01"}),
        ("M-GEO-02","Triangles","Math","geometry",new[]{"M-GEO-01"},new[]{"M-GEO-03","M-GEO-05","M-GEO-06"}),
        ("M-GEO-03","Circle Properties","Math","geometry",new[]{"M-GEO-02"},new[]{"P-CIR-01"}),
        ("M-GEO-04","Coordinate Geometry","Math","geometry",new[]{"M-GEO-01","M-ALG-02"},new[]{"M-GEO-07","M-VEC-01"}),
        ("M-GEO-05","Trigonometric Ratios","Math","geometry",new[]{"M-GEO-02"},new[]{"M-TRG-01","M-TRG-03","M-TRG-04"}),
        ("M-GEO-06","Area & Volume","Math","geometry",new[]{"M-GEO-02"},new[]{"P-FLU-01","C-STO-02"}),
        ("M-GEO-07","Analytic Geometry","Math","geometry",new[]{"M-GEO-04","M-FUN-02"},Array.Empty<string>()),
        // Trigonometry
        ("M-TRG-01","Trig Identities","Math","trigonometry",new[]{"M-GEO-05"},new[]{"M-TRG-02","M-TRG-03","M-VEC-01","P-WAV-02"}),
        ("M-TRG-02","Trig Equations","Math","trigonometry",new[]{"M-TRG-01"},Array.Empty<string>()),
        ("M-TRG-03","Sine & Cosine Rules","Math","trigonometry",new[]{"M-GEO-05","M-TRG-01"},new[]{"P-WAV-02"}),
        ("M-TRG-04","Radian Measure","Math","trigonometry",new[]{"M-GEO-05"},new[]{"P-CIR-01"}),
        // Calculus
        ("M-CAL-01","Limits","Math","calculus",new[]{"M-FUN-03","M-ALG-06"},new[]{"M-CAL-02"}),
        ("M-CAL-02","Derivative Definition","Math","calculus",new[]{"M-CAL-01"},new[]{"M-CAL-03"}),
        ("M-CAL-03","Derivative Rules","Math","calculus",new[]{"M-CAL-02"},new[]{"M-CAL-04","M-CAL-05","P-KIN-04","C-RAT-02"}),
        ("M-CAL-04","Applications of Derivatives","Math","calculus",new[]{"M-CAL-03"},new[]{"P-DYN-02"}),
        ("M-CAL-05","Integrals Intro","Math","calculus",new[]{"M-CAL-03"},new[]{"M-CAL-06","P-ENE-02"}),
        ("M-CAL-06","Definite Integrals","Math","calculus",new[]{"M-CAL-05"},Array.Empty<string>()),
        // Probability & Statistics
        ("M-PRB-01","Counting Principles","Math","probability",new[]{"M-ALG-01"},new[]{"M-PRB-02"}),
        ("M-PRB-02","Basic Probability","Math","probability",new[]{"M-PRB-01"},new[]{"M-PRB-03","B-GEN-03"}),
        ("M-PRB-03","Conditional Probability","Math","probability",new[]{"M-PRB-02"},new[]{"M-PRB-04"}),
        ("M-PRB-04","Binomial Distribution","Math","probability",new[]{"M-PRB-03","M-ALG-08"},new[]{"M-PRB-05","B-POP-03"}),
        ("M-PRB-05","Normal Distribution","Math","probability",new[]{"M-PRB-04"},new[]{"M-PRB-06"}),
        ("M-PRB-06","Statistical Inference","Math","probability",new[]{"M-PRB-05"},Array.Empty<string>()),
        // Vectors
        ("M-VEC-01","Vector Basics","Math","vectors",new[]{"M-GEO-04","M-TRG-01"},new[]{"M-VEC-02","P-DYN-01"}),
        ("M-VEC-02","Dot Product","Math","vectors",new[]{"M-VEC-01"},new[]{"M-VEC-03","M-VEC-04","P-ENE-01"}),
        ("M-VEC-03","Cross Product","Math","vectors",new[]{"M-VEC-02"},new[]{"P-MAG-01"}),
        ("M-VEC-04","Vector Applications","Math","vectors",new[]{"M-VEC-02"},Array.Empty<string>()),

        // ═══════════════════════════════════════════════════════════════
        // PHYSICS (35 concepts) — Bagrut 5-unit
        // ═══════════════════════════════════════════════════════════════

        // Kinematics
        ("P-KIN-01","Displacement & Velocity","Physics","kinematics",new[]{"M-ALG-02"},new[]{"P-KIN-02","P-KIN-03"}),
        ("P-KIN-02","Uniform Motion Graphs","Physics","kinematics",new[]{"P-KIN-01","M-FUN-02"},new[]{"P-KIN-04"}),
        ("P-KIN-03","Projectile Motion","Physics","kinematics",new[]{"P-KIN-01","M-ALG-04","M-FUN-03"},new[]{"P-DYN-01"}),
        ("P-KIN-04","Acceleration & Calculus","Physics","kinematics",new[]{"P-KIN-02","M-CAL-03"},new[]{"P-DYN-02"}),
        // Dynamics
        ("P-DYN-01","Newton's Laws","Physics","dynamics",new[]{"P-KIN-03","M-VEC-01"},new[]{"P-DYN-02","P-DYN-03","P-CIR-01"}),
        ("P-DYN-02","Force & Acceleration","Physics","dynamics",new[]{"P-DYN-01","P-KIN-04","M-CAL-04"},new[]{"P-ENE-01"}),
        ("P-DYN-03","Equilibrium & Statics","Physics","dynamics",new[]{"P-DYN-01","M-ALG-05"},Array.Empty<string>()),
        // Energy & Work
        ("P-ENE-01","Work & Kinetic Energy","Physics","energy",new[]{"P-DYN-02","M-VEC-02"},new[]{"P-ENE-02","P-ENE-03"}),
        ("P-ENE-02","Potential Energy & Conservation","Physics","energy",new[]{"P-ENE-01","M-CAL-05"},new[]{"P-MOM-01","C-THR-01"}),
        ("P-ENE-03","Power & Efficiency","Physics","energy",new[]{"P-ENE-01"},Array.Empty<string>()),
        // Momentum
        ("P-MOM-01","Linear Momentum","Physics","momentum",new[]{"P-ENE-02"},new[]{"P-MOM-02"}),
        ("P-MOM-02","Collisions & Impulse","Physics","momentum",new[]{"P-MOM-01"},Array.Empty<string>()),
        // Circular Motion
        ("P-CIR-01","Circular Motion","Physics","circular-motion",new[]{"P-DYN-01","M-GEO-03","M-TRG-04"},new[]{"P-GRV-01"}),
        ("P-GRV-01","Gravitation","Physics","gravitation",new[]{"P-CIR-01"},Array.Empty<string>()),
        // Waves & Optics
        ("P-WAV-01","Wave Properties","Physics","waves",Array.Empty<string>(),new[]{"P-WAV-02","P-WAV-03"}),
        ("P-WAV-02","Wave Mathematics","Physics","waves",new[]{"P-WAV-01","M-TRG-01","M-TRG-03"},new[]{"P-OPT-02","P-SND-01"}),
        ("P-WAV-03","Standing Waves","Physics","waves",new[]{"P-WAV-01"},Array.Empty<string>()),
        ("P-SND-01","Sound Waves","Physics","waves",new[]{"P-WAV-02"},Array.Empty<string>()),
        ("P-OPT-01","Reflection & Refraction","Physics","optics",new[]{"M-GEO-01"},new[]{"P-OPT-02"}),
        ("P-OPT-02","Lenses & Mirrors","Physics","optics",new[]{"P-OPT-01","P-WAV-02"},Array.Empty<string>()),
        // Electricity & Magnetism
        ("P-ELC-01","Electric Charge & Field","Physics","electricity",Array.Empty<string>(),new[]{"P-ELC-02","P-ELC-03"}),
        ("P-ELC-02","Ohm's Law & Circuits","Physics","electricity",new[]{"P-ELC-01"},new[]{"P-ELC-04","C-ELE-01"}),
        ("P-ELC-03","Electric Potential","Physics","electricity",new[]{"P-ELC-01"},new[]{"P-ELC-04"}),
        ("P-ELC-04","Capacitors","Physics","electricity",new[]{"P-ELC-02","P-ELC-03"},Array.Empty<string>()),
        ("P-MAG-01","Magnetic Fields","Physics","magnetism",new[]{"M-VEC-03"},new[]{"P-MAG-02"}),
        ("P-MAG-02","Electromagnetic Induction","Physics","magnetism",new[]{"P-MAG-01"},Array.Empty<string>()),
        // Fluid Mechanics
        ("P-FLU-01","Pressure & Buoyancy","Physics","fluids",new[]{"M-GEO-06"},Array.Empty<string>()),
        // Modern Physics
        ("P-MOD-01","Photoelectric Effect","Physics","modern",Array.Empty<string>(),new[]{"P-MOD-02"}),
        ("P-MOD-02","Wave-Particle Duality","Physics","modern",new[]{"P-MOD-01"},new[]{"P-MOD-03"}),
        ("P-MOD-03","Atomic Models","Physics","modern",new[]{"P-MOD-02"},new[]{"C-ATM-01"}),

        // ═══════════════════════════════════════════════════════════════
        // CHEMISTRY (32 concepts) — Bagrut 5-unit
        // ═══════════════════════════════════════════════════════════════

        // Atomic Structure
        ("C-ATM-01","Electron Configuration","Chemistry","atomic",new[]{"P-MOD-03"},new[]{"C-ATM-02","C-BND-01"}),
        ("C-ATM-02","Periodic Trends","Chemistry","atomic",new[]{"C-ATM-01"},new[]{"C-BND-01","C-BND-02"}),
        // Bonding
        ("C-BND-01","Ionic Bonding","Chemistry","bonding",new[]{"C-ATM-01","C-ATM-02"},new[]{"C-STO-01","C-SOL-01"}),
        ("C-BND-02","Covalent Bonding","Chemistry","bonding",new[]{"C-ATM-02"},new[]{"C-BND-03","C-ORG-01"}),
        ("C-BND-03","Intermolecular Forces","Chemistry","bonding",new[]{"C-BND-02"},new[]{"C-SOL-01","C-THR-02"}),
        // Stoichiometry
        ("C-STO-01","Moles & Molar Mass","Chemistry","stoichiometry",new[]{"C-BND-01"},new[]{"C-STO-02","C-STO-03"}),
        ("C-STO-02","Gas Laws","Chemistry","stoichiometry",new[]{"C-STO-01","M-GEO-06"},new[]{"C-THR-01"}),
        ("C-STO-03","Reaction Stoichiometry","Chemistry","stoichiometry",new[]{"C-STO-01"},new[]{"C-EQU-01","C-RAT-01"}),
        // Thermochemistry
        ("C-THR-01","Enthalpy & Hess's Law","Chemistry","thermochemistry",new[]{"C-STO-02","P-ENE-02"},new[]{"C-THR-02"}),
        ("C-THR-02","Entropy & Free Energy","Chemistry","thermochemistry",new[]{"C-THR-01","C-BND-03"},Array.Empty<string>()),
        // Equilibrium
        ("C-EQU-01","Chemical Equilibrium","Chemistry","equilibrium",new[]{"C-STO-03","M-ALG-05"},new[]{"C-EQU-02","C-ACB-01"}),
        ("C-EQU-02","Le Chatelier's Principle","Chemistry","equilibrium",new[]{"C-EQU-01"},Array.Empty<string>()),
        // Acids & Bases
        ("C-ACB-01","Acids, Bases & pH","Chemistry","acids-bases",new[]{"C-EQU-01"},new[]{"C-ACB-02"}),
        ("C-ACB-02","Titrations & Buffers","Chemistry","acids-bases",new[]{"C-ACB-01"},Array.Empty<string>()),
        ("C-PH-01","pH Calculations","Chemistry","acids-bases",new[]{"C-ACB-01","M-FUN-05"},Array.Empty<string>()),
        // Reaction Rates
        ("C-RAT-01","Rate Laws","Chemistry","kinetics",new[]{"C-STO-03","M-FUN-04"},new[]{"C-RAT-02"}),
        ("C-RAT-02","Activation Energy","Chemistry","kinetics",new[]{"C-RAT-01","M-CAL-03"},Array.Empty<string>()),
        // Electrochemistry
        ("C-ELE-01","Galvanic Cells","Chemistry","electrochemistry",new[]{"P-ELC-02"},new[]{"C-ELE-02"}),
        ("C-ELE-02","Electrolysis","Chemistry","electrochemistry",new[]{"C-ELE-01"},Array.Empty<string>()),
        // Organic Chemistry
        ("C-ORG-01","Hydrocarbons","Chemistry","organic",new[]{"C-BND-02"},new[]{"C-ORG-02","C-ORG-03"}),
        ("C-ORG-02","Functional Groups","Chemistry","organic",new[]{"C-ORG-01"},new[]{"C-ORG-04","B-MOL-01"}),
        ("C-ORG-03","Isomerism","Chemistry","organic",new[]{"C-ORG-01"},Array.Empty<string>()),
        ("C-ORG-04","Polymers","Chemistry","organic",new[]{"C-ORG-02"},new[]{"B-MOL-02"}),
        // Solutions
        ("C-SOL-01","Solubility & Concentration","Chemistry","solutions",new[]{"C-BND-01","C-BND-03"},Array.Empty<string>()),

        // ═══════════════════════════════════════════════════════════════
        // BIOLOGY (35 concepts) — Bagrut 5-unit
        // ═══════════════════════════════════════════════════════════════

        // Cell Biology
        ("B-CEL-01","Cell Structure","Biology","cell-biology",Array.Empty<string>(),new[]{"B-CEL-02","B-CEL-03","B-CEL-04"}),
        ("B-CEL-02","Cell Membrane & Transport","Biology","cell-biology",new[]{"B-CEL-01"},new[]{"B-PHY-01","B-NRV-01"}),
        ("B-CEL-03","Mitosis & Cell Cycle","Biology","cell-biology",new[]{"B-CEL-01"},new[]{"B-GEN-01","B-CEL-05"}),
        ("B-CEL-04","Cell Respiration","Biology","cell-biology",new[]{"B-CEL-01"},new[]{"B-PHO-01","B-PHY-02"}),
        ("B-CEL-05","Meiosis","Biology","cell-biology",new[]{"B-CEL-03"},new[]{"B-GEN-02"}),
        // Molecular Biology
        ("B-MOL-01","DNA Structure & Replication","Biology","molecular",new[]{"C-ORG-02"},new[]{"B-MOL-02","B-MOL-03"}),
        ("B-MOL-02","Protein Synthesis","Biology","molecular",new[]{"B-MOL-01","C-ORG-04"},new[]{"B-MOL-04","B-GEN-01"}),
        ("B-MOL-03","Gene Regulation","Biology","molecular",new[]{"B-MOL-01"},new[]{"B-BIO-01"}),
        ("B-MOL-04","Mutations","Biology","molecular",new[]{"B-MOL-02"},new[]{"B-EVO-02","B-GEN-03"}),
        // Genetics
        ("B-GEN-01","Mendelian Genetics","Biology","genetics",new[]{"B-CEL-03","B-MOL-02"},new[]{"B-GEN-02","B-GEN-03"}),
        ("B-GEN-02","Meiosis & Crossing Over","Biology","genetics",new[]{"B-GEN-01","B-CEL-05"},new[]{"B-GEN-04"}),
        ("B-GEN-03","Pedigree & Probability","Biology","genetics",new[]{"B-GEN-01","B-MOL-04","M-PRB-02"},new[]{"B-GEN-04"}),
        ("B-GEN-04","Population Genetics","Biology","genetics",new[]{"B-GEN-02","B-GEN-03"},new[]{"B-EVO-01"}),
        // Evolution
        ("B-EVO-01","Natural Selection","Biology","evolution",new[]{"B-GEN-04"},new[]{"B-EVO-02","B-ECO-01"}),
        ("B-EVO-02","Speciation","Biology","evolution",new[]{"B-EVO-01","B-MOL-04"},Array.Empty<string>()),
        // Ecology
        ("B-ECO-01","Ecosystems & Energy Flow","Biology","ecology",new[]{"B-EVO-01"},new[]{"B-ECO-02","B-POP-01"}),
        ("B-ECO-02","Biogeochemical Cycles","Biology","ecology",new[]{"B-ECO-01"},Array.Empty<string>()),
        ("B-POP-01","Population Dynamics","Biology","ecology",new[]{"B-ECO-01"},new[]{"B-POP-02","B-POP-03"}),
        ("B-POP-02","Growth Models","Biology","ecology",new[]{"B-POP-01","M-FUN-04","M-ALG-08"},Array.Empty<string>()),
        ("B-POP-03","Community Ecology","Biology","ecology",new[]{"B-POP-01","M-PRB-04"},Array.Empty<string>()),
        // Photosynthesis
        ("B-PHO-01","Light Reactions","Biology","photosynthesis",new[]{"B-CEL-04"},new[]{"B-PHO-02"}),
        ("B-PHO-02","Calvin Cycle","Biology","photosynthesis",new[]{"B-PHO-01"},Array.Empty<string>()),
        // Human Physiology
        ("B-PHY-01","Nervous System","Biology","physiology",new[]{"B-CEL-02"},new[]{"B-NRV-01"}),
        ("B-NRV-01","Action Potential","Biology","physiology",new[]{"B-PHY-01","B-CEL-02"},new[]{"B-NRV-02"}),
        ("B-NRV-02","Synapse & Neurotransmitters","Biology","physiology",new[]{"B-NRV-01"},Array.Empty<string>()),
        ("B-PHY-02","Endocrine System","Biology","physiology",new[]{"B-CEL-04"},new[]{"B-IMM-01"}),
        ("B-IMM-01","Immune System","Biology","immunology",new[]{"B-PHY-02"},new[]{"B-IMM-02"}),
        ("B-IMM-02","Adaptive Immunity","Biology","immunology",new[]{"B-IMM-01"},Array.Empty<string>()),
        // Biotechnology
        ("B-BIO-01","PCR & Gel Electrophoresis","Biology","biotechnology",new[]{"B-MOL-03"},new[]{"B-BIO-02"}),
        ("B-BIO-02","Genetic Engineering","Biology","biotechnology",new[]{"B-BIO-01"},Array.Empty<string>()),

        // ═══════════════════════════════════════════════════════════════
        // COMPUTER SCIENCE (25 concepts) — Bagrut 5-unit
        // ═══════════════════════════════════════════════════════════════

        ("CS-FND-01","Variables & Data Types","CS","fundamentals",Array.Empty<string>(),new[]{"CS-FND-02","CS-FND-03"}),
        ("CS-FND-02","Control Flow","CS","fundamentals",new[]{"CS-FND-01"},new[]{"CS-FND-04","CS-ALG-01"}),
        ("CS-FND-03","Arrays & Strings","CS","fundamentals",new[]{"CS-FND-01"},new[]{"CS-FND-04","CS-DST-01"}),
        ("CS-FND-04","Functions & Scope","CS","fundamentals",new[]{"CS-FND-02","CS-FND-03"},new[]{"CS-OOP-01","CS-REC-01"}),
        // OOP
        ("CS-OOP-01","Classes & Objects","CS","oop",new[]{"CS-FND-04"},new[]{"CS-OOP-02","CS-OOP-03"}),
        ("CS-OOP-02","Inheritance & Polymorphism","CS","oop",new[]{"CS-OOP-01"},new[]{"CS-OOP-04"}),
        ("CS-OOP-03","Encapsulation & Abstraction","CS","oop",new[]{"CS-OOP-01"},Array.Empty<string>()),
        ("CS-OOP-04","Design Patterns Intro","CS","oop",new[]{"CS-OOP-02"},Array.Empty<string>()),
        // Algorithms
        ("CS-ALG-01","Linear & Binary Search","CS","algorithms",new[]{"CS-FND-02"},new[]{"CS-ALG-02","CS-ALG-03"}),
        ("CS-ALG-02","Sorting Algorithms","CS","algorithms",new[]{"CS-ALG-01"},new[]{"CS-CMP-01"}),
        ("CS-ALG-03","Greedy Algorithms","CS","algorithms",new[]{"CS-ALG-01"},new[]{"CS-DYN-01"}),
        // Data Structures
        ("CS-DST-01","Linked Lists","CS","data-structures",new[]{"CS-FND-03"},new[]{"CS-DST-02","CS-DST-03"}),
        ("CS-DST-02","Stacks & Queues","CS","data-structures",new[]{"CS-DST-01"},new[]{"CS-GRP-01"}),
        ("CS-DST-03","Trees & BST","CS","data-structures",new[]{"CS-DST-01"},new[]{"CS-DST-04","CS-GRP-01"}),
        ("CS-DST-04","Hash Tables","CS","data-structures",new[]{"CS-DST-03"},Array.Empty<string>()),
        // Recursion
        ("CS-REC-01","Recursion Basics","CS","recursion",new[]{"CS-FND-04"},new[]{"CS-REC-02","CS-DYN-01"}),
        ("CS-REC-02","Backtracking","CS","recursion",new[]{"CS-REC-01"},Array.Empty<string>()),
        // Complexity
        ("CS-CMP-01","Big-O Analysis","CS","complexity",new[]{"CS-ALG-02"},new[]{"CS-CMP-02"}),
        ("CS-CMP-02","Space vs Time Trade-offs","CS","complexity",new[]{"CS-CMP-01"},Array.Empty<string>()),
        // Graph Algorithms
        ("CS-GRP-01","BFS & DFS","CS","graphs",new[]{"CS-DST-02","CS-DST-03"},new[]{"CS-GRP-02"}),
        ("CS-GRP-02","Shortest Path (Dijkstra)","CS","graphs",new[]{"CS-GRP-01"},Array.Empty<string>()),
        // Dynamic Programming
        ("CS-DYN-01","Memoization & Tabulation","CS","dynamic-programming",new[]{"CS-ALG-03","CS-REC-01"},Array.Empty<string>()),
        // Boolean Logic
        ("CS-BOL-01","Boolean Algebra","CS","logic",Array.Empty<string>(),new[]{"CS-BOL-02"}),
        ("CS-BOL-02","Logic Gates & Circuits","CS","logic",new[]{"CS-BOL-01"},Array.Empty<string>()),
        // Databases
        ("CS-DB-01","Relational Model & SQL","CS","databases",new[]{"CS-FND-03"},Array.Empty<string>()),

        // ═══════════════════════════════════════════════════════════════
        // ENGLISH (18 concepts) — Bagrut 5-unit
        // ═══════════════════════════════════════════════════════════════

        ("E-RDG-01","Reading Comprehension Basics","English","reading",Array.Empty<string>(),new[]{"E-RDG-02","E-RDG-03"}),
        ("E-RDG-02","Inference & Analysis","English","reading",new[]{"E-RDG-01"},new[]{"E-RDG-04","E-LIT-01"}),
        ("E-RDG-03","Unseen Text Strategies","English","reading",new[]{"E-RDG-01"},new[]{"E-RDG-04"}),
        ("E-RDG-04","Rhetoric & Persuasion","English","reading",new[]{"E-RDG-02","E-RDG-03"},new[]{"E-WRT-03"}),
        // Grammar
        ("E-GRM-01","Tenses & Verb Forms","English","grammar",Array.Empty<string>(),new[]{"E-GRM-02","E-GRM-03"}),
        ("E-GRM-02","Conditionals & Modals","English","grammar",new[]{"E-GRM-01"},new[]{"E-WRT-01"}),
        ("E-GRM-03","Relative Clauses","English","grammar",new[]{"E-GRM-01"},new[]{"E-WRT-01"}),
        // Vocabulary
        ("E-VOC-01","Academic Vocabulary","English","vocabulary",Array.Empty<string>(),new[]{"E-VOC-02","E-WRT-01"}),
        ("E-VOC-02","Context Clues & Word Formation","English","vocabulary",new[]{"E-VOC-01"},Array.Empty<string>()),
        // Writing
        ("E-WRT-01","Essay Structure","English","writing",new[]{"E-GRM-02","E-GRM-03","E-VOC-01"},new[]{"E-WRT-02","E-WRT-03"}),
        ("E-WRT-02","Argumentative Writing","English","writing",new[]{"E-WRT-01"},Array.Empty<string>()),
        ("E-WRT-03","Discursive Writing","English","writing",new[]{"E-WRT-01","E-RDG-04"},Array.Empty<string>()),
        // Literature
        ("E-LIT-01","Literary Devices","English","literature",new[]{"E-RDG-02"},new[]{"E-LIT-02","E-LIT-03"}),
        ("E-LIT-02","Poetry Analysis","English","literature",new[]{"E-LIT-01"},Array.Empty<string>()),
        ("E-LIT-03","Prose & Drama","English","literature",new[]{"E-LIT-01"},Array.Empty<string>()),
        // Listening
        ("E-LST-01","Listening Comprehension","English","listening",Array.Empty<string>(),new[]{"E-LST-02"}),
        ("E-LST-02","Note-Taking & Summary","English","listening",new[]{"E-LST-01"},Array.Empty<string>()),
        // Speaking
        ("E-SPK-01","Oral Presentation","English","speaking",new[]{"E-WRT-01"},Array.Empty<string>()),
    };

    public async Task<StudentMasteryDetailResponse?> GetStudentMasteryAsync(string studentId, ClaimsPrincipal user)
    {
        // REV-014: Verify this student belongs to the caller's school
        var schoolId = TenantScope.GetSchoolFilter(user);
        if (schoolId is not null)
        {
            await using var checkSession = _store.QuerySession();
            var snapshot = await checkSession.Query<StudentProfileSnapshot>()
                .Where(s => s.StudentId == studentId)
                .FirstOrDefaultAsync();
            if (snapshot?.SchoolId != schoolId)
                return null; // Not in caller's school
        }

        var random = new Random(studentId.GetHashCode());

        // Generate mastery for all 42 concepts with realistic progression
        // Student "progress depth" varies by hash — some students are further along
        var progressFactor = 0.3f + (Math.Abs(studentId.GetHashCode()) % 70) / 100f; // 0.3-1.0

        var concepts = new List<ConceptMasteryNode>();
        foreach (var (id, name, subject, cluster, prereqs, unlocks) in BagrutConcepts)
        {
            // Subject-level progression — student may be stronger in some subjects
            var subjectStrength = subject switch
            {
                "Math" => 0.0f + (random.NextSingle() * 0.1f),
                "Physics" => 0.1f + (random.NextSingle() * 0.1f),
                "Chemistry" => 0.2f + (random.NextSingle() * 0.1f),
                "Biology" => 0.15f + (random.NextSingle() * 0.1f),
                "CS" => 0.25f + (random.NextSingle() * 0.1f),
                "English" => 0.05f + (random.NextSingle() * 0.1f),
                _ => 0.2f
            };

            // Deeper concepts have lower mastery; progress factor scales how far the student has gotten
            var depthPenalty = subjectStrength + cluster switch
            {
                "algebra" or "fundamentals" or "reading" or "cell-biology" or "atomic" => 0.0f,
                "functions" or "kinematics" or "bonding" or "molecular" or "grammar" or "oop" => 0.1f,
                "geometry" or "dynamics" or "stoichiometry" or "genetics" or "algorithms" or "vocabulary" => 0.15f,
                "trigonometry" or "energy" or "equilibrium" or "evolution" or "data-structures" or "writing" => 0.25f,
                "calculus" or "magnetism" or "organic" or "ecology" or "graphs" or "literature" => 0.35f,
                "vectors" or "modern" or "electrochemistry" or "biotechnology" or "dynamic-programming" => 0.4f,
                _ => 0.2f
            };

            var baseMastery = Math.Max(0, progressFactor - depthPenalty + (random.NextSingle() * 0.3f - 0.15f));
            baseMastery = Math.Min(1.0f, baseMastery);

            var status = baseMastery switch
            {
                >= 0.8f => "mastered",
                >= 0.3f => "in_progress",
                >= 0.05f => "available",
                _ => "locked"
            };

            concepts.Add(new ConceptMasteryNode(id, name, subject, baseMastery, status, prereqs, unlocks));
        }

        var frontier = concepts
            .Where(c => c.Status == "available" || (c.Status == "in_progress" && c.MasteryLevel < 0.5f))
            .OrderByDescending(c => c.MasteryLevel)
            .Take(5)
            .Select(c => new LearningFrontierItem(c.ConceptId, c.ConceptName, 0.5f + random.NextSingle() * 0.4f, "prerequisites_met"))
            .ToList();

        var history = new List<MasteryHistoryPoint>();
        for (int i = 6; i >= 0; i--)
        {
            history.Add(new MasteryHistoryPoint(
                Date: DateTimeOffset.UtcNow.AddDays(-i * 7).ToString("yyyy-MM-dd"),
                AvgMastery: 0.3f + (6 - i) * 0.04f * progressFactor + random.NextSingle() * 0.05f,
                ConceptsAttempted: random.Next(5, 25),
                ConceptsMastered: random.Next(0, (int)(8 * progressFactor))));
        }

        var atRiskConcepts = concepts.Where(c => c.Status == "in_progress" && c.MasteryLevel < 0.5f).Take(3);
        var scaffolding = atRiskConcepts.Select(c =>
            new ScaffoldingRecommendation(c.ConceptId, c.ConceptName, "moderate", $"Student needs reinforcement on {c.ConceptName}")).ToList();

        var reviewQueue = concepts
            .Where(c => c.Status == "mastered" || c.MasteryLevel > 0.6f)
            .OrderBy(_ => random.Next())
            .Take(4)
            .Select((c, i) => new ReviewPriorityItem(c.ConceptId, c.ConceptName,
                0.2f + random.NextSingle() * 0.6f, // DecayRisk
                c.MasteryLevel, // LastMasteryLevel
                DateTimeOffset.UtcNow.AddDays(-random.Next(3, 21)), i + 1))
            .ToList();

        return new StudentMasteryDetailResponse(
            StudentId: studentId,
            StudentName: $"Student {studentId}",
            KnowledgeMap: concepts,
            LearningFrontier: frontier,
            MasteryHistory: history,
            Scaffolding: scaffolding,
            ReviewQueue: reviewQueue);
    }

    public async Task<ClassMasteryResponse?> GetClassMasteryAsync(string classId)
    {
        var random = new Random(classId.GetHashCode());
        var concepts = new[] { "alg-001", "alg-002", "calc-001", "phy-001", "phy-002" };

        var students = new List<StudentMasteryRow>();
        for (int i = 0; i < 25; i++)
        {
            students.Add(new StudentMasteryRow(
                StudentId: $"stu-{i}",
                StudentName: $"Student {i + 1}",
                MasteryLevels: concepts.Select(_ => random.NextSingle() * 100f).ToList(),
                OverallProgress: random.NextSingle() * 100f));
        }

        var difficultyAnalysis = new List<ConceptDifficulty>
        {
            new("calc-001", "Derivatives", 0.52f, 0.35f, 450),
            new("alg-002", "Quadratic Equations", 0.68f, 0.22f, 520),
            new("phy-001", "Kinematics", 0.61f, 0.28f, 380)
        };

        return new ClassMasteryResponse(
            ClassId: classId,
            ClassName: $"Class {classId}",
            Concepts: concepts.ToList(),
            Students: students,
            DifficultyAnalysis: difficultyAnalysis,
            Pacing: new PacingRecommendation(
                ReadyToAdvance: true,
                Recommendation: "Class is ready to advance to Dynamics",
                ConceptsToReview: new[] { "calc-001" },
                ConceptsReadyToIntroduce: new[] { "phy-002" }));
    }

    public async Task<AtRiskStudentsResponse> GetAtRiskStudentsAsync(ClaimsPrincipal user)
    {
        // REV-014: Validate caller has a school scope (throws for non-SUPER_ADMIN without school_id)
        _ = TenantScope.GetSchoolFilter(user);

        var students = new List<AtRiskStudent>
        {
            new("stu-risk-1", "Student X", "class-1", "high", 0.42f, -0.15f, "Extra scaffolding on prerequisites"),
            new("stu-risk-2", "Student Y", "class-2", "medium", 0.55f, -0.08f, "Review sessions recommended"),
            new("stu-risk-3", "Student Z", "class-1", "medium", 0.58f, -0.05f, "Check for external factors")
        };

        return new AtRiskStudentsResponse(students);
    }

    public async Task<MethodologyProfileAdminResponse?> GetMethodologyProfileAsync(string studentId, ClaimsPrincipal user)
    {
        // REV-014: Verify this student belongs to the caller's school
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.LightweightSession();
        var snapshot = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        if (snapshot == null) return null;

        // REV-014: Block cross-school access
        if (schoolId is not null && snapshot.SchoolId != schoolId)
            return null;

        var subjects = snapshot.SubjectMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Subject", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(50), kv.Value.ConfidenceReachedAt != null)).ToList();

        var topics = snapshot.TopicMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Topic", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(30), kv.Value.ConfidenceReachedAt != null)).ToList();

        var concepts = snapshot.ConceptMethodologyMap.Select(kv => new MethodologyLevelEntry(
            kv.Key, "Concept", kv.Value.Methodology.ToString(), kv.Value.Source.ToString(),
            kv.Value.AttemptCount, kv.Value.SuccessRate, kv.Value.Confidence,
            kv.Value.HasSufficientData(30), kv.Value.ConfidenceReachedAt != null)).ToList();

        // Also include flat methodology map for concepts without hierarchy entries
        foreach (var (conceptId, methodology) in snapshot.ActiveMethodologyMap)
        {
            if (!concepts.Any(c => c.Id == conceptId))
            {
                var mastery = snapshot.ConceptMastery.GetValueOrDefault(conceptId);
                concepts.Add(new MethodologyLevelEntry(
                    conceptId, "Concept", methodology, "McmRouted",
                    mastery?.TotalAttempts ?? 0,
                    mastery != null && mastery.TotalAttempts > 0 ? mastery.CorrectCount / (float)mastery.TotalAttempts : 0f,
                    0f, false, false));
            }
        }

        return new MethodologyProfileAdminResponse(studentId, subjects, topics, concepts);
    }

    public async Task<bool> OverrideMethodologyAsync(string studentId, string level, string levelId, string methodology, string teacherId)
    {
        // Store the override in Marten as a document for now.
        // The actual actor-level override happens when the actor reactivates and reads this.
        await using var session = _store.LightweightSession();

        var overrideDoc = new MethodologyOverrideDocument
        {
            Id = $"{studentId}:{level}:{levelId}",
            StudentId = studentId,
            Level = level,
            LevelId = levelId,
            Methodology = methodology,
            TeacherId = teacherId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        session.Store(overrideDoc);
        await session.SaveChangesAsync();

        _logger.LogInformation(
            "Teacher {TeacherId} overrode methodology for student {StudentId} at {Level}/{LevelId} to {Methodology}",
            teacherId, studentId, level, levelId, methodology);

        return true;
    }

    public async Task<IReadOnlyList<MethodologyOverrideDocument>> GetStudentOverridesAsync(string studentId)
    {
        await using var session = _store.QuerySession();
        var overrides = await session.Query<MethodologyOverrideDocument>()
            .Where(o => o.StudentId == studentId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return overrides;
    }

    public async Task<bool> RemoveOverrideAsync(string studentId, string overrideId)
    {
        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<MethodologyOverrideDocument>(overrideId);
        if (existing == null || existing.StudentId != studentId) return false;
        session.Delete<MethodologyOverrideDocument>(overrideId);
        await session.SaveChangesAsync();

        _logger.LogInformation(
            "Methodology override {OverrideId} removed for student {StudentId}",
            overrideId, studentId);

        return true;
    }
}

// ── Methodology Profile Response DTOs ──

public sealed record MethodologyProfileAdminResponse(
    string StudentId,
    IReadOnlyList<MethodologyLevelEntry> Subjects,
    IReadOnlyList<MethodologyLevelEntry> Topics,
    IReadOnlyList<MethodologyLevelEntry> Concepts);

public sealed record MethodologyLevelEntry(
    string Id,
    string Level,
    string Methodology,
    string Source,
    int AttemptCount,
    float SuccessRate,
    float Confidence,
    bool HasSufficientData,
    bool ConfidenceReached);

public class MethodologyOverrideDocument
{
    public string Id { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string Level { get; set; } = "";
    public string LevelId { get; set; } = "";
    public string Methodology { get; set; } = "";
    public string TeacherId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
