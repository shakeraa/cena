// =============================================================================
// Quality Gate Labeled Test Data
// 50 GOOD questions + 50 BAD questions across all dimensions
// Each labeled with expected gate decision and which dimensions should flag
// =============================================================================

using Cena.Api.Contracts.Admin.QualityGate;
using QualityGateOption = Cena.Api.Contracts.Admin.QualityGate.QualityGateOption;

namespace Cena.Admin.Api.Tests.QualityGate;

public static class QualityGateTestData
{
    /// <summary>
    /// Labeled test cases: each has an input, expected decision, and expected flagged dimensions.
    /// </summary>
    public static IEnumerable<LabeledTestCase> GetAll()
    {
        foreach (var tc in GetGoodQuestions()) yield return tc;
        foreach (var tc in GetBadQuestions()) yield return tc;
    }

    public static IEnumerable<LabeledTestCase> GetGoodQuestions()
    {
        // G01: Well-formed math MCQ — Bloom 3 (Apply), difficulty 0.5
        yield return new("G01", GateDecision.NeedsReview, // NeedsReview because LLM dimensions default to review range
            MakeInput("G01", "Solve for x: 2x + 6 = 14", "Math", "he", 3, 0.5f,
                ("4", true, null),
                ("8", false, "Adds instead of subtracting"),
                ("3", false, "Divides before subtracting"),
                ("20", false, "Adds all numbers")));

        // G02: Physics calculation — Bloom 3, difficulty 0.6
        yield return new("G02", GateDecision.NeedsReview,
            MakeInput("G02", "A car accelerates from rest at 2 m/s². What is its velocity after 5 seconds?", "Physics", "en", 3, 0.6f,
                ("10 m/s", true, null),
                ("7 m/s", false, "Uses wrong formula: v = a + t"),
                ("25 m/s", false, "Uses d = ½at² instead of v = at"),
                ("2.5 m/s", false, "Divides instead of multiplying")));

        // G03: Chemistry — Bloom 2 (Understand), difficulty 0.3
        yield return new("G03", GateDecision.NeedsReview,
            MakeInput("G03", "What is the pH of a neutral solution at 25°C?", "Chemistry", "he", 2, 0.3f,
                ("7", true, null),
                ("0", false, "Confuses neutral with strongly acidic"),
                ("14", false, "Confuses neutral with strongly basic"),
                ("1", false, "Confuses with pOH")));

        // G04: Biology — Bloom 4 (Analyze), difficulty 0.7
        yield return new("G04", GateDecision.NeedsReview,
            MakeInput("G04", "Compare the processes of mitosis and meiosis. Which statement correctly identifies a key difference?", "Biology", "en", 4, 0.7f,
                ("Meiosis produces 4 haploid cells while mitosis produces 2 diploid cells", true, null),
                ("Mitosis occurs only in reproductive cells", false, "Reverses the roles of mitosis and meiosis"),
                ("Meiosis has no crossing over phase", false, "Crossing over is a key feature of meiosis"),
                ("Both processes produce identical daughter cells", false, "Only mitosis produces identical cells")));

        // G05: CS — Bloom 1 (Remember), difficulty 0.2
        yield return new("G05", GateDecision.NeedsReview,
            MakeInput("G05", "What is the time complexity of binary search on a sorted array?", "Computer Science", "en", 1, 0.2f,
                ("O(log n)", true, null),
                ("O(n)", false, "Confuses with linear search"),
                ("O(n²)", false, "Confuses with bubble sort"),
                ("O(1)", false, "Confuses with hash table lookup")));

        // G06: English — Bloom 2, difficulty 0.4
        yield return new("G06", GateDecision.NeedsReview,
            MakeInput("G06", "Identify the literary device used in: 'The wind whispered through the ancient trees'", "English", "en", 2, 0.4f,
                ("Personification", true, null),
                ("Simile", false, "No comparison with 'like' or 'as'"),
                ("Hyperbole", false, "No exaggeration present"),
                ("Alliteration", false, "Not focused on repeated initial sounds")));

        // G07: Math integral — Bloom 3, difficulty 0.8
        yield return new("G07", GateDecision.NeedsReview,
            MakeInput("G07", "Calculate the definite integral ∫₀² (3x² + 1)dx", "Math", "he", 3, 0.8f,
                ("10", true, null),
                ("9", false, "Forgets the constant term integration"),
                ("12", false, "Evaluates at upper bound only"),
                ("8", false, "Uses wrong power rule")));

        // G08: Physics waves — Bloom 3, difficulty 0.5
        yield return new("G08", GateDecision.NeedsReview,
            MakeInput("G08", "A wave has frequency 500 Hz and wavelength 0.68 m. Calculate the wave speed.", "Physics", "en", 3, 0.5f,
                ("340 m/s", true, null),
                ("735 m/s", false, "Divides frequency by wavelength"),
                ("0.00136 m/s", false, "Divides wavelength by frequency"),
                ("500.68 m/s", false, "Adds frequency and wavelength")));

        // G09: Chemistry balancing — Bloom 3, difficulty 0.6
        yield return new("G09", GateDecision.NeedsReview,
            MakeInput("G09", "Balance the equation: __Fe + __O₂ → __Fe₂O₃", "Chemistry", "ar", 3, 0.6f,
                ("4Fe + 3O₂ → 2Fe₂O₃", true, null),
                ("2Fe + 3O₂ → Fe₂O₃", false, "Incorrect Fe coefficient"),
                ("4Fe + 2O₂ → 2Fe₂O₃", false, "Incorrect O₂ coefficient"),
                ("Fe + O₂ → Fe₂O₃", false, "Not balanced at all")));

        // G10: Biology genetics — Bloom 3, difficulty 0.5
        yield return new("G10", GateDecision.NeedsReview,
            MakeInput("G10", "In a monohybrid cross of Aa × Aa, what is the expected genotype ratio?", "Biology", "he", 3, 0.5f,
                ("1:2:1 (AA:Aa:aa)", true, null),
                ("3:1 (AA:aa)", false, "Confuses genotype with phenotype ratio"),
                ("1:1 (Aa:aa)", false, "Confuses with test cross"),
                ("2:2 (AA:Aa)", false, "Ignores homozygous recessive")));

        // G11-G20: More well-formed questions across subjects
        yield return new("G11", GateDecision.NeedsReview,
            MakeInput("G11", "Find the equation of the line passing through (1, 3) and (4, 9)", "Math", "he", 3, 0.4f,
                ("y = 2x + 1", true, null),
                ("y = 3x - 1", false, "Wrong slope calculation"),
                ("y = 2x + 3", false, "Wrong y-intercept"),
                ("y = x + 2", false, "Slope = 1 error")));

        yield return new("G12", GateDecision.NeedsReview,
            MakeInput("G12", "Calculate the kinetic energy of a 3 kg object moving at 4 m/s", "Physics", "en", 3, 0.4f,
                ("24 J", true, null),
                ("12 J", false, "Forgets the ½ factor or uses wrong formula"),
                ("48 J", false, "Doubles instead of halving"),
                ("6 J", false, "Uses KE = mv instead of ½mv²")));

        yield return new("G13", GateDecision.NeedsReview,
            MakeInput("G13", "How many moles of NaCl are in 117 g of sodium chloride? (Molar mass NaCl = 58.5 g/mol)", "Chemistry", "he", 3, 0.3f,
                ("2 mol", true, null),
                ("0.5 mol", false, "Divides in wrong direction"),
                ("117 mol", false, "Doesn't divide by molar mass"),
                ("58.5 mol", false, "Uses molar mass as answer")));

        yield return new("G14", GateDecision.NeedsReview,
            MakeInput("G14", "Explain the role of ribosomes in protein synthesis", "Biology", "en", 2, 0.3f,
                ("Ribosomes translate mRNA into amino acid chains", true, null),
                ("Ribosomes transcribe DNA into mRNA", false, "Confuses translation with transcription"),
                ("Ribosomes store genetic information", false, "Confuses with nucleus function"),
                ("Ribosomes break down proteins", false, "Confuses with proteasomes")));

        yield return new("G15", GateDecision.NeedsReview,
            MakeInput("G15", "What is the worst-case time complexity of quicksort?", "Computer Science", "en", 1, 0.3f,
                ("O(n²)", true, null),
                ("O(n log n)", false, "This is average case, not worst case"),
                ("O(log n)", false, "Confuses with binary search"),
                ("O(n)", false, "Confuses with linear scan")));

        yield return new("G16", GateDecision.NeedsReview,
            MakeInput("G16", "Choose the correct form: If she ___ harder, she would have passed the exam.", "English", "en", 3, 0.4f,
                ("had studied", true, null),
                ("studied", false, "Wrong tense for third conditional"),
                ("would study", false, "Mixes conditional structures"),
                ("has studied", false, "Uses present perfect instead of past perfect")));

        yield return new("G17", GateDecision.NeedsReview,
            MakeInput("G17", "Solve the quadratic equation: x² - 5x + 6 = 0", "Math", "ar", 3, 0.4f,
                ("x = 2, x = 3", true, null),
                ("x = -2, x = -3", false, "Sign error in factoring"),
                ("x = 1, x = 6", false, "Wrong factoring: 1×6 instead of 2×3"),
                ("x = 5, x = 1", false, "Confuses sum and product")));

        yield return new("G18", GateDecision.NeedsReview,
            MakeInput("G18", "Calculate the resistance of a circuit with voltage 12V and current 3A", "Physics", "he", 3, 0.3f,
                ("4 Ω", true, null),
                ("36 Ω", false, "Multiplies V×I instead of dividing"),
                ("0.25 Ω", false, "Divides I/V instead of V/I"),
                ("15 Ω", false, "Adds V+I")));

        yield return new("G19", GateDecision.NeedsReview,
            MakeInput("G19", "What type of bond forms between Na and Cl in NaCl?", "Chemistry", "en", 1, 0.2f,
                ("Ionic bond", true, null),
                ("Covalent bond", false, "Confuses with molecular compounds"),
                ("Metallic bond", false, "Confuses with metal-metal bonding"),
                ("Hydrogen bond", false, "Confuses with intermolecular forces")));

        yield return new("G20", GateDecision.NeedsReview,
            MakeInput("G20", "Describe the function of white blood cells in the immune system", "Biology", "he", 2, 0.3f,
                ("They identify and destroy pathogens and foreign substances", true, null),
                ("They transport oxygen throughout the body", false, "Confuses with red blood cells"),
                ("They help blood clotting at wound sites", false, "Confuses with platelets"),
                ("They carry nutrients to cells", false, "Confuses with plasma")));

        // G21-G30: Hebrew and Arabic questions
        yield return new("G21", GateDecision.NeedsReview,
            MakeInput("G21", "פתור את המשוואה: 3x - 7 = 14", "Math", "he", 3, 0.3f,
                ("x = 7", true, null),
                ("x = 3", false, "שגיאת חישוב בחילוק"),
                ("x = 21", false, "שוכח לחלק ב-3"),
                ("x = -7", false, "שגיאת סימן")));

        yield return new("G22", GateDecision.NeedsReview,
            MakeInput("G22", "احسب مساحة مثلث قاعدته 8 سم وارتفاعه 5 سم", "Math", "ar", 3, 0.3f,
                ("20 سم²", true, null),
                ("40 سم²", false, "نسي القسمة على 2"),
                ("13 سم²", false, "جمع القاعدة والارتفاع"),
                ("3 سم²", false, "طرح بدلاً من الضرب")));

        yield return new("G23", GateDecision.NeedsReview,
            MakeInput("G23", "מהי ההתנגדות השקולה של שלוש נגדים בטור: 2Ω, 3Ω, 5Ω?", "Physics", "he", 3, 0.3f,
                ("10 Ω", true, null),
                ("0.97 Ω", false, "מחשב כאילו מקבילים"),
                ("30 Ω", false, "כופל במקום לחבר"),
                ("3.33 Ω", false, "מחשב ממוצע")));

        yield return new("G24", GateDecision.NeedsReview,
            MakeInput("G24", "ما هو الرقم الهيدروجيني لمحلول حمض الهيدروكلوريك بتركيز 0.01 مول/لتر؟", "Chemistry", "ar", 3, 0.5f,
                ("2", true, null),
                ("12", false, "يخلط بين pH و pOH"),
                ("0.01", false, "يستخدم التركيز مباشرة"),
                ("7", false, "يعتبره محلولاً متعادلاً")));

        yield return new("G25", GateDecision.NeedsReview,
            MakeInput("G25", "הסבר את תפקיד המיטוכונדריה בתא", "Biology", "he", 2, 0.3f,
                ("ייצור אנרגיה (ATP) דרך נשימה תאית", true, null),
                ("פוטוסינתזה וייצור סוכרים", false, "מבלבל עם כלורופלסט"),
                ("אחסון חומר גנטי", false, "מבלבל עם גרעין התא"),
                ("פירוק חלבונים", false, "מבלבל עם ליזוזום")));

        // G26-G50: More varied good questions
        yield return new("G26", GateDecision.NeedsReview,
            MakeInput("G26", "Find the probability of rolling a sum of 7 with two standard dice", "Math", "en", 3, 0.5f,
                ("1/6", true, null),
                ("1/12", false, "Counts only 3 combinations instead of 6"),
                ("7/36", false, "Counts 7 instead of 6 favorable outcomes"),
                ("1/36", false, "Counts only one specific pair")));

        yield return new("G27", GateDecision.NeedsReview,
            MakeInput("G27", "A satellite orbits Earth at height h above the surface. Which expression gives the orbital velocity?", "Physics", "en", 4, 0.8f,
                ("v = √(GM/(R+h))", true, null),
                ("v = √(GM/h)", false, "Uses h instead of R+h"),
                ("v = GM/(R+h)²", false, "Confuses with gravitational force formula"),
                ("v = 2π(R+h)/T", false, "Correct but requires knowing T")));

        yield return new("G28", GateDecision.NeedsReview,
            MakeInput("G28", "Identify the oxidizing agent in the reaction: Zn + CuSO₄ → ZnSO₄ + Cu", "Chemistry", "en", 4, 0.5f,
                ("Cu²⁺ (from CuSO₄)", true, null),
                ("Zn", false, "Confuses oxidizing and reducing agents"),
                ("SO₄²⁻", false, "Sulfate is a spectator ion"),
                ("ZnSO₄", false, "This is a product, not a reactant")));

        yield return new("G29", GateDecision.NeedsReview,
            MakeInput("G29", "Explain why antibiotics are ineffective against viral infections", "Biology", "en", 5, 0.6f,
                ("Viruses lack cell walls and ribosomes that antibiotics target", true, null),
                ("Viruses are too small for antibiotics to reach", false, "Size is not the issue"),
                ("Antibiotics only work on fungi and parasites", false, "Antibiotics target bacteria"),
                ("Viruses mutate too quickly for antibiotics", false, "Confuses with antibiotic resistance")));

        yield return new("G30", GateDecision.NeedsReview,
            MakeInput("G30", "Trace the execution of BFS starting from vertex A in the graph: A-B, A-C, B-D, C-D, D-E", "Computer Science", "en", 3, 0.6f,
                ("A, B, C, D, E", true, null),
                ("A, B, D, E, C", false, "Processes D before C (DFS-like)"),
                ("A, C, B, D, E", false, "Wrong neighbor order"),
                ("A, B, C, E, D", false, "Visits E before D")));

        // G31-G50: Fill remaining good questions
        yield return new("G31", GateDecision.NeedsReview,
            MakeInput("G31", "Determine the slope of the line tangent to y = x³ at x = 2", "Math", "en", 3, 0.6f,
                ("12", true, null), ("8", false, "Uses f(2) not f'(2)"), ("6", false, "Wrong derivative"), ("3", false, "Forgets power rule")));

        yield return new("G32", GateDecision.NeedsReview,
            MakeInput("G32", "Calculate the wavelength of light with frequency 6×10¹⁴ Hz (c = 3×10⁸ m/s)", "Physics", "en", 3, 0.5f,
                ("5×10⁻⁷ m", true, null), ("2×10⁶ m", false, "Multiplied instead of divided"), ("5×10⁷ m", false, "Wrong exponent"), ("1.8×10²³ m", false, "Multiplied f×c")));

        yield return new("G33", GateDecision.NeedsReview,
            MakeInput("G33", "What is the hybridization of the central carbon in methane (CH₄)?", "Chemistry", "en", 2, 0.3f,
                ("sp³", true, null), ("sp²", false, "That's for 3 bonds"), ("sp", false, "That's for 2 bonds"), ("d²sp³", false, "That's for octahedral")));

        yield return new("G34", GateDecision.NeedsReview,
            MakeInput("G34", "Which organelle is responsible for packaging and shipping proteins?", "Biology", "en", 1, 0.2f,
                ("Golgi apparatus", true, null), ("Endoplasmic reticulum", false, "ER synthesizes, Golgi packages"), ("Lysosome", false, "Lysosomes break down"), ("Ribosome", false, "Ribosomes synthesize")));

        yield return new("G35", GateDecision.NeedsReview,
            MakeInput("G35", "What design pattern separates object construction from its representation?", "Computer Science", "en", 2, 0.4f,
                ("Builder", true, null), ("Factory", false, "Factory creates objects but doesn't separate steps"), ("Singleton", false, "Singleton restricts instances"), ("Observer", false, "Observer handles events")));

        yield return new("G36", GateDecision.NeedsReview,
            MakeInput("G36", "Rewrite in passive voice: The students completed the assignment on time.", "English", "en", 3, 0.3f,
                ("The assignment was completed on time by the students", true, null), ("The assignment completed by the students on time", false, "Missing auxiliary verb"), ("On time the students completed the assignment", false, "Just reordered, still active"), ("The assignment is completed on time", false, "Wrong tense")));

        yield return new("G37", GateDecision.NeedsReview,
            MakeInput("G37", "חשב את הנגזרת של f(x) = sin(2x)", "Math", "he", 3, 0.5f,
                ("2cos(2x)", true, null), ("cos(2x)", false, "שוכח כלל שרשרת"), ("-2cos(2x)", false, "שגיאת סימן"), ("2sin(2x)", false, "לא מחליף ל-cos")));

        yield return new("G38", GateDecision.NeedsReview,
            MakeInput("G38", "Explain how natural selection leads to adaptation over generations", "Biology", "en", 5, 0.7f,
                ("Organisms with advantageous traits survive and reproduce more, passing those traits to offspring", true, null),
                ("Organisms deliberately change their genes to adapt", false, "Confuses with Lamarckian evolution"),
                ("Only the strongest organisms survive in every generation", false, "Oversimplified 'survival of the fittest'"),
                ("Random mutations always improve an organism's fitness", false, "Mutations are random, not always beneficial")));

        yield return new("G39", GateDecision.NeedsReview,
            MakeInput("G39", "أوجد قيمة x إذا كان: log₂(x) = 5", "Math", "ar", 3, 0.5f,
                ("32", true, null), ("10", false, "يخلط مع log₁₀"), ("25", false, "يضرب 2×5 بدلاً من 2⁵"), ("7", false, "يجمع 2+5")));

        yield return new("G40", GateDecision.NeedsReview,
            MakeInput("G40", "Solve the inequality: 2x - 3 > 7", "Math", "en", 3, 0.3f,
                ("x > 5", true, null), ("x > 2", false, "Doesn't add 3 first"), ("x < 5", false, "Wrong inequality direction"), ("x > 10", false, "Doesn't divide by 2")));

        yield return new("G41", GateDecision.NeedsReview,
            MakeInput("G41", "Calculate the acceleration due to gravity on a planet with mass 2M and radius 3R relative to Earth", "Physics", "en", 4, 0.8f,
                ("2g/9", true, null), ("6g", false, "Multiplies mass and radius"), ("2g/3", false, "Forgets to square radius"), ("g/3", false, "Only considers radius change")));

        yield return new("G42", GateDecision.NeedsReview,
            MakeInput("G42", "Write an SQL query to find students with grades above 85 in Mathematics", "Computer Science", "en", 3, 0.4f,
                ("SELECT * FROM students WHERE subject='Math' AND grade > 85", true, null),
                ("SELECT * FROM students WHERE grade > 85", false, "Missing subject filter"),
                ("SELECT grade FROM students WHERE subject='Math'", false, "Missing grade condition"),
                ("SELECT * FROM students HAVING grade > 85", false, "HAVING without GROUP BY")));

        yield return new("G43", GateDecision.NeedsReview,
            MakeInput("G43", "Identify the type of reaction: 2H₂O₂ → 2H₂O + O₂", "Chemistry", "en", 2, 0.3f,
                ("Decomposition", true, null), ("Synthesis", false, "Confuses with combination"), ("Single displacement", false, "No element replacing another"), ("Double displacement", false, "No ion exchange")));

        yield return new("G44", GateDecision.NeedsReview,
            MakeInput("G44", "What is the function of DNA polymerase during replication?", "Biology", "en", 2, 0.3f,
                ("It adds nucleotides to the growing DNA strand", true, null),
                ("It unwinds the double helix", false, "That's helicase"),
                ("It joins Okazaki fragments", false, "That's DNA ligase"),
                ("It removes RNA primers", false, "That's a different enzyme")));

        yield return new("G45", GateDecision.NeedsReview,
            MakeInput("G45", "Find the area under f(x) = 2x + 1 from x = 0 to x = 3", "Math", "en", 3, 0.5f,
                ("12", true, null), ("7", false, "Only evaluates f(3)"), ("9", false, "Forgets the constant"), ("21", false, "Multiplies f(3) by 3")));

        yield return new("G46", GateDecision.NeedsReview,
            MakeInput("G46", "Determine the resultant force when forces of 3N east and 4N north act on an object", "Physics", "en", 3, 0.5f,
                ("5 N", true, null), ("7 N", false, "Adds magnitudes directly"), ("1 N", false, "Subtracts magnitudes"), ("12 N", false, "Multiplies magnitudes")));

        yield return new("G47", GateDecision.NeedsReview,
            MakeInput("G47", "Explain the difference between an array and a linked list in terms of memory allocation", "Computer Science", "en", 4, 0.5f,
                ("Arrays use contiguous memory; linked lists use scattered nodes connected by pointers", true, null),
                ("Arrays are always faster than linked lists", false, "Not always true for insertions"),
                ("Linked lists use less total memory than arrays", false, "Pointer overhead makes this false"),
                ("Both use the same memory layout", false, "Fundamentally different structures")));

        yield return new("G48", GateDecision.NeedsReview,
            MakeInput("G48", "מצא את שיפוע הישר העובר דרך הנקודות (0,0) ו-(6,3)", "Math", "he", 3, 0.2f,
                ("0.5", true, null), ("2", false, "הפוך את היחס"), ("3", false, "משתמש רק ב-y"), ("6", false, "משתמש רק ב-x")));

        yield return new("G49", GateDecision.NeedsReview,
            MakeInput("G49", "Calculate the period of a pendulum with length 2.5 m (g = 10 m/s²)", "Physics", "en", 3, 0.5f,
                ("~3.14 s", true, null), ("~1.57 s", false, "Forgets 2π factor"), ("~5 s", false, "Uses wrong formula"), ("~0.5 s", false, "Inverts the formula")));

        yield return new("G50", GateDecision.NeedsReview,
            MakeInput("G50", "حلل العوامل: x² - 9", "Math", "ar", 3, 0.3f,
                ("(x+3)(x-3)", true, null), ("(x-3)²", false, "يعتبره مربعاً كاملاً"), ("(x+9)(x-1)", false, "تحليل خاطئ"), ("(x-3)(x-3)", false, "يكرر نفس العامل")));
    }

    public static IEnumerable<LabeledTestCase> GetBadQuestions()
    {
        // B01: Empty stem
        yield return new("B01", GateDecision.AutoRejected,
            MakeInput("B01", "", "Math", "he", 3, 0.5f,
                ("4", true, null), ("8", false, null), ("3", false, null), ("20", false, null)),
            ExpectedFlags: new[] { "STEM_EMPTY" });

        // B02: No correct answer
        yield return new("B02", GateDecision.AutoRejected,
            MakeInputNoCorrect("B02", "What is 2 + 2?", "Math", "en", 1, 0.1f,
                "3", "5", "6", "7"),
            ExpectedFlags: new[] { "NO_CORRECT_ANSWER" });

        // B03: Multiple correct answers
        yield return new("B03", GateDecision.AutoRejected,
            MakeInput("B03", "Which number is even?", "Math", "en", 1, 0.1f,
                ("2", true, null), ("4", true, null), ("3", false, null), ("5", false, null)),
            ExpectedFlags: new[] { "MULTIPLE_CORRECT" });

        // B04: Only 1 option
        yield return new("B04", GateDecision.AutoRejected,
            MakeInputCustomOptions("B04", "Solve: x = 5", "Math", "en", 1, 0.1f,
                new QualityGateOption[] { new("A", "5", true, null) }),
            ExpectedFlags: new[] { "TOO_FEW_OPTIONS" });

        // B05: Duplicate options
        yield return new("B05", GateDecision.AutoRejected,
            MakeInput("B05", "What is the capital of Israel?", "English", "en", 1, 0.1f,
                ("Jerusalem", true, null), ("Tel Aviv", false, null), ("Jerusalem", false, null), ("Haifa", false, null)),
            ExpectedFlags: new[] { "DUPLICATE_OPTIONS" });

        // B06: Empty option text
        yield return new("B06", GateDecision.AutoRejected,
            MakeInput("B06", "Calculate 10 × 5", "Math", "en", 3, 0.3f,
                ("50", true, null), ("", false, null), ("15", false, null), ("500", false, null)),
            ExpectedFlags: new[] { "EMPTY_OPTIONS" });

        // B07: Invalid Bloom level (0)
        yield return new("B07", GateDecision.AutoRejected,
            MakeInput("B07", "Solve: 2 + 2", "Math", "en", 0, 0.1f,
                ("4", true, null), ("3", false, null), ("5", false, null), ("6", false, null)),
            ExpectedFlags: new[] { "INVALID_BLOOM" });

        // B08: Invalid Bloom level (7)
        yield return new("B08", GateDecision.AutoRejected,
            MakeInput("B08", "Solve: 3 + 3", "Math", "en", 7, 0.1f,
                ("6", true, null), ("5", false, null), ("7", false, null), ("9", false, null)),
            ExpectedFlags: new[] { "INVALID_BLOOM" });

        // B09: Invalid difficulty (-0.5)
        yield return new("B09", GateDecision.AutoRejected,
            MakeInput("B09", "What is 1 + 1?", "Math", "en", 1, -0.5f,
                ("2", true, null), ("1", false, null), ("3", false, null), ("0", false, null)),
            ExpectedFlags: new[] { "INVALID_DIFFICULTY" });

        // B10: Invalid subject
        yield return new("B10", GateDecision.AutoRejected,
            MakeInput("B10", "What is the meaning of life?", "Philosophy", "en", 5, 0.9f,
                ("42", true, null), ("Nothing", false, null), ("Love", false, null), ("Everything", false, null)),
            ExpectedFlags: new[] { "INVALID_SUBJECT" });

        // B11: Invalid language
        yield return new("B11", GateDecision.AutoRejected,
            MakeInput("B11", "Qu'est-ce que c'est?", "English", "fr", 1, 0.1f,
                ("C'est un chat", true, null), ("Un chien", false, null), ("Un oiseau", false, null), ("Un poisson", false, null)),
            ExpectedFlags: new[] { "INVALID_LANGUAGE" });

        // B12: Very short stem (< 10 chars)
        yield return new("B12", GateDecision.AutoRejected,
            MakeInput("B12", "x = ?", "Math", "en", 1, 0.1f,
                ("5", true, null), ("3", false, null), ("7", false, null), ("1", false, null)),
            ExpectedFlags: new[] { "STEM_TOO_SHORT" });

        // B13: "All of the above" option
        yield return new("B13", GateDecision.NeedsReview, // Warning, not critical
            MakeInput("B13", "Which of these are prime numbers?", "Math", "en", 1, 0.2f,
                ("All of the above", true, null), ("2", false, null), ("3", false, null), ("4", false, null)),
            ExpectedFlags: new[] { "ALL_NONE_ABOVE" });

        // B14: Extremely long correct answer vs short distractors
        yield return new("B14", GateDecision.NeedsReview,
            MakeInput("B14", "What causes tides on Earth?", "Physics", "en", 2, 0.4f,
                ("The gravitational pull of the Moon and Sun on Earth's oceans creates bulges on opposite sides of Earth, resulting in two high tides and two low tides each day as Earth rotates", true, null),
                ("Wind", false, null), ("Rotation", false, null), ("Currents", false, null)),
            ExpectedFlags: new[] { "CORRECT_LONGER" });

        // B15: Bloom mismatch — claims level 5 (Evaluate) but stem is "What is" (Remember)
        yield return new("B15", GateDecision.NeedsReview,
            MakeInput("B15", "What is the formula for the area of a circle?", "Math", "en", 5, 0.2f,
                ("πr²", true, null), ("2πr", false, null), ("πd", false, null), ("r²", false, null)),
            ExpectedFlags: new[] { "TIER_MISMATCH" });

        // B16: Bloom mismatch — claims level 1 (Remember) but stem is "Analyze and compare"
        // Heuristic detects ADJACENT_TIER (Middle vs Lower) because "analyze" matches middle patterns
        yield return new("B16", GateDecision.NeedsReview,
            MakeInput("B16", "Analyze and compare the effectiveness of three different sorting algorithms for large datasets", "Computer Science", "en", 1, 0.8f,
                ("QuickSort is generally fastest on average but MergeSort guarantees O(n log n)", true, null),
                ("BubbleSort is always fastest", false, null), ("All are equal", false, null), ("HeapSort is always worst", false, null)),
            ExpectedFlags: new[] { "ADJACENT_TIER" });

        // B17: Hebrew "כל התשובות" (all of the above)
        yield return new("B17", GateDecision.NeedsReview,
            MakeInput("B17", "אילו מהמספרים הבאים ראשוניים?", "Math", "he", 1, 0.2f,
                ("כל התשובות נכונות", true, null), ("2", false, null), ("3", false, null), ("5", false, null)),
            ExpectedFlags: new[] { "ALL_NONE_ABOVE" });

        // B18: Arabic "لا شيء مما سبق" (none of the above)
        yield return new("B18", GateDecision.NeedsReview,
            MakeInput("B18", "ما هو حاصل ضرب 0 في أي عدد؟", "Math", "ar", 1, 0.1f,
                ("لا شيء مما سبق", true, null), ("1", false, null), ("العدد نفسه", false, null), ("غير معرف", false, null)),
            ExpectedFlags: new[] { "ALL_NONE_ABOVE" });

        // B19: Difficulty > 1.0
        yield return new("B19", GateDecision.AutoRejected,
            MakeInput("B19", "Solve: 5 × 5", "Math", "en", 3, 1.5f,
                ("25", true, null), ("10", false, null), ("30", false, null), ("20", false, null)),
            ExpectedFlags: new[] { "INVALID_DIFFICULTY" });

        // B20: CorrectOptionIndex out of range
        yield return new("B20", GateDecision.AutoRejected,
            MakeInputWithIndex("B20", "What is 3 + 4?", "Math", "en", 1, 0.1f, 5,
                new QualityGateOption("A", "7", true, null),
                new QualityGateOption("B", "6", false, null),
                new QualityGateOption("C", "8", false, null),
                new QualityGateOption("D", "5", false, null)),
            ExpectedFlags: new[] { "INVALID_CORRECT_INDEX" });

        // B21: Near-duplicate distractors — Jaccard similarity catches these
        // Also has LENGTH_DISPARITY and LENGTH_MISMATCH due to very long distractors vs short correct
        yield return new("B21", GateDecision.NeedsReview,
            MakeInput("B21", "Calculate the velocity of light in vacuum", "Physics", "en", 1, 0.2f,
                ("3 × 10⁸ m/s", true, null),
                ("The speed is approximately 3 × 10⁵ km/s which equals roughly 300000 km per second", false, "Wrong unit"),
                ("The speed is about 3 × 10⁵ km/s or approximately 300000 kilometers per second", false, "Same error different words"),
                ("1.5 × 10⁸ m/s", false, "Half the actual speed")),
            ExpectedFlags: new[] { "LENGTH_DISPARITY" });

        // B22: Negative stem without emphasis
        yield return new("B22", GateDecision.NeedsReview,
            MakeInput("B22", "Which of the following is NOT a noble gas?", "Chemistry", "en", 1, 0.3f,
                ("Nitrogen", true, null), ("Helium", false, null), ("Neon", false, null), ("Argon", false, null)),
            ExpectedFlags: new[] { "NEGATIVE_STEM" });

        // B23: Two options only
        yield return new("B23", GateDecision.AutoRejected,
            MakeInputCustomOptions("B23", "Is water wet?", "Chemistry", "en", 1, 0.1f,
                new QualityGateOption[] {
                    new("A", "Yes", true, null),
                    new("B", "No", false, null)
                }),
            ExpectedFlags: new[] { "TOO_FEW_OPTIONS" });

        // B24: Correct answer with no distractor rationale on any
        yield return new("B24", GateDecision.NeedsReview,
            MakeInput("B24", "Find the derivative of f(x) = 5x³", "Math", "en", 3, 0.4f,
                ("15x²", true, null), ("5x²", false, null), ("15x³", false, null), ("3x²", false, null)),
            ExpectedFlags: new[] { "NO_RATIONALE" });

        // B25-B50: More bad questions with various defects

        // B25: Stem is just a number
        yield return new("B25", GateDecision.AutoRejected,
            MakeInput("B25", "42", "Math", "en", 1, 0.1f,
                ("Yes", true, null), ("No", false, null), ("Maybe", false, null), ("N/A", false, null)),
            ExpectedFlags: new[] { "STEM_TOO_SHORT" });

        // B26: All distractors empty
        yield return new("B26", GateDecision.AutoRejected,
            MakeInput("B26", "What is the speed of sound in air at room temperature?", "Physics", "en", 1, 0.3f,
                ("343 m/s", true, null), ("", false, null), ("", false, null), ("", false, null)),
            ExpectedFlags: new[] { "EMPTY_OPTIONS" });

        // B27: 7 options (too many)
        yield return new("B27", GateDecision.NeedsReview, // Warning, not critical
            MakeInputCustomOptions("B27", "Which element has atomic number 6?", "Chemistry", "en", 1, 0.2f,
                new QualityGateOption[] {
                    new("A", "Carbon", true, null), new("B", "Nitrogen", false, null),
                    new("C", "Oxygen", false, null), new("D", "Boron", false, null),
                    new("E", "Lithium", false, null), new("F", "Beryllium", false, null)
                }),
            ExpectedFlags: new[] { "TOO_MANY_OPTIONS" });

        // B28: CorrectOptionIndex mismatch with IsCorrect
        yield return new("B28", GateDecision.AutoRejected,
            MakeInputWithIndex("B28", "What is 10 - 3?", "Math", "en", 1, 0.1f, 2,
                new QualityGateOption("A", "7", true, null),
                new QualityGateOption("B", "6", false, null),
                new QualityGateOption("C", "8", false, null),
                new QualityGateOption("D", "5", false, null)),
            ExpectedFlags: new[] { "INDEX_MISMATCH" });

        // B29: Stem with grammatical cue (a/an)
        yield return new("B29", GateDecision.NeedsReview,
            MakeInput("B29", "The process of photosynthesis occurs in an", "Biology", "en", 1, 0.2f,
                ("organelle called the chloroplast", true, null),
                ("mitochondrion", false, null),
                ("nucleus", false, null),
                ("ribosome", false, null)),
            ExpectedFlags: new[] { "GRAMMATICAL_CUE" });

        // B30: Math question where distractors don't have numbers but correct does
        yield return new("B30", GateDecision.NeedsReview,
            MakeInput("B30", "Calculate the circumference of a circle with radius 5 cm", "Math", "en", 3, 0.3f,
                ("31.4 cm", true, null),
                ("It depends on the diameter", false, null),
                ("Cannot be determined", false, null),
                ("The answer varies", false, null)),
            ExpectedFlags: new[] { "MISSING_NUMERIC_DISTRACTORS" });

        // B31-B50: Additional systematic defects
        yield return new("B31", GateDecision.AutoRejected,
            MakeInput("B31", "   ", "Math", "en", 3, 0.5f, // whitespace-only stem
                ("4", true, null), ("8", false, "err"), ("3", false, "err"), ("20", false, "err")),
            ExpectedFlags: new[] { "STEM_EMPTY" });

        yield return new("B32", GateDecision.AutoRejected,
            MakeInput("B32", "Good question stem here about physics", "Physics", "de", 3, 0.5f, // German language
                ("Answer", true, null), ("Wrong1", false, "r"), ("Wrong2", false, "r"), ("Wrong3", false, "r")),
            ExpectedFlags: new[] { "INVALID_LANGUAGE" });

        yield return new("B33", GateDecision.AutoRejected,
            MakeInput("B33", "Solve this equation carefully", "Art", "en", 3, 0.5f, // Invalid subject
                ("42", true, null), ("41", false, "r"), ("43", false, "r"), ("44", false, "r")),
            ExpectedFlags: new[] { "INVALID_SUBJECT" });

        yield return new("B34", GateDecision.AutoRejected,
            MakeInput("B34", "Find x in the equation 2x = 10", "Math", "en", 3, 2.0f, // difficulty > 1
                ("5", true, null), ("10", false, "r"), ("2", false, "r"), ("20", false, "r")),
            ExpectedFlags: new[] { "INVALID_DIFFICULTY" });

        yield return new("B35", GateDecision.AutoRejected,
            MakeInput("B35", "What is 7 × 8?", "Math", "en", -1, 0.3f, // negative Bloom
                ("56", true, null), ("54", false, "r"), ("58", false, "r"), ("48", false, "r")),
            ExpectedFlags: new[] { "INVALID_BLOOM" });

        yield return new("B36", GateDecision.AutoRejected,
            MakeInput("B36", "Important physics question about motion", "Math", "en", 3, 0.5f,
                ("10 m/s", true, null), ("10 m/s", false, "same as correct"), ("10 m/s", false, "same as correct"), ("10 m/s", false, "same as correct")),
            ExpectedFlags: new[] { "DUPLICATE_OPTIONS" });

        yield return new("B37", GateDecision.NeedsReview,
            MakeInput("B37", "Define the term 'acceleration' in physics", "Physics", "en", 5, 0.2f, // Says Evaluate but is Remember
                ("Rate of change of velocity", true, null), ("Speed", false, "common confusion"), ("Force", false, "different concept"), ("Distance", false, "wrong dimension")),
            ExpectedFlags: new[] { "TIER_MISMATCH" });

        yield return new("B38", GateDecision.NeedsReview,
            MakeInput("B38", "Evaluate and synthesize the implications of quantum mechanics on modern computing paradigms", "Physics", "en", 1, 0.9f, // Says Remember but is clearly Higher
                ("Quantum computing uses superposition for parallel processing", true, null),
                ("Computers use electricity", false, "r"), ("Binary is 0 and 1", false, "r"), ("RAM stores data", false, "r")),
            ExpectedFlags: new[] { "TIER_MISMATCH" });

        yield return new("B39", GateDecision.NeedsReview,
            MakeInput("B39", "Which is NEVER true about parallel lines?", "Math", "en", 2, 0.4f,
                ("They intersect", true, null), ("Same slope", false, null), ("Equal distance", false, null), ("In same plane", false, null)),
            ExpectedFlags: new[] { "NEGATIVE_STEM" });

        yield return new("B40", GateDecision.AutoRejected,
            MakeInput("B40", "Hi", "English", "en", 1, 0.1f, // Stem too short
                ("Hello", true, null), ("Bye", false, null), ("Thanks", false, null), ("Sorry", false, null)),
            ExpectedFlags: new[] { "STEM_TOO_SHORT" });

        yield return new("B41", GateDecision.AutoRejected,
            MakeInputCustomOptions("B41", "Solve this equation for all real values of x", "Math", "en", 3, 0.5f,
                new QualityGateOption[] { new("A", "5", true, null), new("B", "3", false, null) }), // Only 2 options
            ExpectedFlags: new[] { "TOO_FEW_OPTIONS" });

        // B42: Hebrew "אף תשובה" in STEM (not options) — structural checker only checks options
        // The real flag detected is NEAR_DUPLICATE_DISTRACTORS (short similar distractors) and MISSING_NUMERIC_DISTRACTORS
        yield return new("B42", GateDecision.NeedsReview,
            MakeInput("B42", "אף תשובה אינה נכונה — מהי הנגזרת של x²?", "Math", "he", 3, 0.3f,
                ("2x", true, null), ("x", false, "r"), ("x²", false, "r"), ("2", false, "r")),
            ExpectedFlags: new[] { "NEAR_DUPLICATE_DISTRACTORS" });

        yield return new("B43", GateDecision.AutoRejected,
            MakeInputWithIndex("B43", "Find the area of a square with side 4 cm", "Math", "en", 3, 0.2f, -1, // Negative index
                new QualityGateOption("A", "16 cm²", true, null),
                new QualityGateOption("B", "8 cm²", false, null),
                new QualityGateOption("C", "12 cm²", false, null),
                new QualityGateOption("D", "4 cm²", false, null)),
            ExpectedFlags: new[] { "INVALID_CORRECT_INDEX" });

        yield return new("B44", GateDecision.NeedsReview,
            MakeInput("B44", "The cell membrane is made of a", "Biology", "en", 1, 0.2f, // Grammatical cue with "a"
                ("phospholipid bilayer with embedded proteins and cholesterol molecules", true, null),
                ("outer wall", false, null),
                ("empty space", false, null),
                ("organic compound", false, null)),
            ExpectedFlags: new[] { "CORRECT_LONGER" }); // Correct is much longer

        // B45: Two long distractors are similar; correct answer is very short relative to distractors
        yield return new("B45", GateDecision.NeedsReview,
            MakeInput("B45", "Calculate the molarity of a solution containing 40g NaOH in 500mL", "Chemistry", "en", 3, 0.5f,
                ("2 M", true, null),
                ("The molarity depends on the amount of solute dissolved in a given volume of solution measured in moles per liter", false, null),
                ("The molarity is calculated by dividing moles of solute by liters of solution which gives concentration", false, null),
                ("0.5 M", false, "Inverts the calculation")),
            ExpectedFlags: new[] { "CORRECT_TOO_SHORT" });

        yield return new("B46", GateDecision.AutoRejected,
            MakeInput("B46", "Good stem", "Math", "en", 3, 0.5f, // < 10 chars
                ("A", true, null), ("B", false, null), ("C", false, null), ("D", false, null)),
            ExpectedFlags: new[] { "STEM_TOO_SHORT" });

        yield return new("B47", GateDecision.NeedsReview,
            MakeInput("B47", "Name the three states of matter", "Chemistry", "en", 6, 0.1f, // Claims Create (6) but is Remember
                ("Solid, liquid, gas", true, null),
                ("Hot, cold, warm", false, "Confuses with temperature"),
                ("Heavy, light, medium", false, "Confuses with weight"),
                ("Big, small, tiny", false, "Confuses with size")),
            ExpectedFlags: new[] { "TIER_MISMATCH" });

        yield return new("B48", GateDecision.AutoRejected,
            MakeInput("B48", "Solve: 100 ÷ 4", "Math", "jp", 3, 0.3f, // Invalid language "jp"
                ("25", true, null), ("20", false, "r"), ("30", false, "r"), ("50", false, "r")),
            ExpectedFlags: new[] { "INVALID_LANGUAGE" });

        yield return new("B49", GateDecision.AutoRejected,
            MakeInput("B49", "What is the derivative of e^x?", "Math", "en", 8, 0.5f, // Bloom level 8 invalid
                ("e^x", true, null), ("xe^(x-1)", false, "r"), ("1/x", false, "r"), ("ln(x)", false, "r")),
            ExpectedFlags: new[] { "INVALID_BLOOM" });

        yield return new("B50", GateDecision.AutoRejected,
            MakeInput("B50", "What year did World War II end?", "History", "en", 1, 0.2f, // Invalid subject
                ("1945", true, null), ("1944", false, "r"), ("1946", false, "r"), ("1943", false, "r")),
            ExpectedFlags: new[] { "INVALID_SUBJECT" });
    }

    // Helper methods
    private static QualityGateInput MakeInput(string id, string stem, string subject, string lang, int bloom, float diff,
        (string text, bool correct, string? rationale) opt1,
        (string text, bool correct, string? rationale) opt2,
        (string text, bool correct, string? rationale) opt3,
        (string text, bool correct, string? rationale) opt4)
    {
        var options = new List<QualityGateOption>
        {
            new("A", opt1.text, opt1.correct, opt1.rationale),
            new("B", opt2.text, opt2.correct, opt2.rationale),
            new("C", opt3.text, opt3.correct, opt3.rationale),
            new("D", opt4.text, opt4.correct, opt4.rationale)
        };
        int correctIdx = options.FindIndex(o => o.IsCorrect);
        return new(id, stem, options, correctIdx, subject, lang, bloom, diff, "4 Units", null);
    }

    private static QualityGateInput MakeInputNoCorrect(string id, string stem, string subject, string lang, int bloom, float diff,
        string opt1, string opt2, string opt3, string opt4)
    {
        var options = new List<QualityGateOption>
        {
            new("A", opt1, false, null), new("B", opt2, false, null),
            new("C", opt3, false, null), new("D", opt4, false, null)
        };
        return new(id, stem, options, 0, subject, lang, bloom, diff, "4 Units", null);
    }

    private static QualityGateInput MakeInputCustomOptions(string id, string stem, string subject, string lang, int bloom, float diff,
        QualityGateOption[] options)
    {
        int correctIdx = Array.FindIndex(options, o => o.IsCorrect);
        return new(id, stem, options, Math.Max(0, correctIdx), subject, lang, bloom, diff, "4 Units", null);
    }

    private static QualityGateInput MakeInputWithIndex(string id, string stem, string subject, string lang, int bloom, float diff,
        int correctIndex, params QualityGateOption[] options)
    {
        return new(id, stem, options.ToList(), correctIndex, subject, lang, bloom, diff, "4 Units", null);
    }
}

public sealed record LabeledTestCase(
    string Id,
    GateDecision ExpectedDecision,
    QualityGateInput Input,
    string[]? ExpectedFlags = null);
