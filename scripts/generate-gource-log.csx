#!/usr/bin/env dotnet-script
// =============================================================================
// Generate Gource Custom Log from Cena Student Simulation Data
// Maps student concept attempts to a filesystem metaphor for visualization.
//
// Gource format: unix_timestamp|username|action|filepath|color
//   action: A=first attempt, M=mastery progress, D=failed/regression
//   path: Subject/Topic/Concept (tree structure)
//
// Usage: dotnet script scripts/generate-gource-log.csx > /tmp/cena-student-graph.log
//        gource /tmp/cena-student-graph.log --title "Cena Student Concept Graph" ...
// =============================================================================

// Since dotnet-script may not be available, we generate the log as a standalone approach.
// This script outputs the gource log format to stdout.

Console.Error.WriteLine("Generating Cena student concept graph for gource...");

var random = new Random(42);
var now = DateTimeOffset.UtcNow;
var startDate = now.AddDays(-60);

// Curriculum structure: Subject/Topic/Concept
var curriculum = new Dictionary<string, Dictionary<string, string[]>>
{
    ["Math"] = new()
    {
        ["Algebra"] = new[] { "Number Properties", "Linear Equations", "Inequalities", "Quadratic Equations", "Systems of Equations", "Polynomials", "Rational Expressions", "Sequences & Series" },
        ["Functions"] = new[] { "Function Basics", "Linear Functions", "Quadratic Functions", "Exponential Functions", "Logarithmic Functions", "Composite Functions", "Inverse Functions" },
        ["Geometry"] = new[] { "Angles & Lines", "Triangles", "Circle Properties", "Coordinate Geometry", "Trig Ratios", "Area & Volume", "Analytic Geometry" },
        ["Trigonometry"] = new[] { "Trig Identities", "Trig Equations", "Sine & Cosine Rules", "Radian Measure" },
        ["Calculus"] = new[] { "Limits", "Derivative Definition", "Derivative Rules", "Applications of Derivatives", "Integrals Intro", "Definite Integrals" },
        ["Probability"] = new[] { "Counting Principles", "Basic Probability", "Conditional Probability", "Binomial Distribution", "Normal Distribution", "Statistical Inference" },
        ["Vectors"] = new[] { "Vector Basics", "Dot Product", "Cross Product", "Vector Applications" }
    }
};

// Student archetypes with behavior profiles
var archetypes = new (string Name, int Count, float AccuracyMean, float SessionsPerDay, string Color)[]
{
    ("Genius",          5,  0.92f, 2.5f, "00FF00"),
    ("HighAchiever",   10,  0.82f, 2.0f, "7367F0"),
    ("SteadyLearner",  30,  0.68f, 1.5f, "28C76F"),
    ("Struggling",     15,  0.45f, 1.0f, "FF9F43"),
    ("FastCareless",   10,  0.55f, 2.5f, "00CFE8"),
    ("SlowThorough",   10,  0.72f, 0.8f, "A8AAAE"),
    ("Inconsistent",   10,  0.50f, 1.2f, "FFD54F"),
    ("VeryLowCog",     10,  0.30f, 0.6f, "EA5455"),
};

// All concept paths
var allPaths = new List<(string Path, int Depth)>();
foreach (var (subject, topics) in curriculum)
{
    foreach (var (topic, concepts) in topics)
    {
        foreach (var concept in concepts)
        {
            allPaths.Add(($"{subject}/{topic}/{concept}", 0));
        }
    }
}

var events = new List<(long Timestamp, string User, string Action, string Path, string Color)>();

int studentId = 0;
foreach (var (archName, count, accuracy, sessionsPerDay, color) in archetypes)
{
    for (int s = 0; s < count; s++)
    {
        studentId++;
        var studentName = $"{archName}-{s + 1:D2}";
        var studentRng = new Random(42 + studentId * 1000);

        // Each student progresses through concepts over 60 days
        // Higher accuracy = more concepts reached, deeper in tree
        int maxConcepts = (int)(allPaths.Count * (0.3f + accuracy * 0.7f));
        var conceptOrder = allPaths.OrderBy(_ => studentRng.Next()).Take(maxConcepts).ToList();

        for (int day = 0; day < 60; day++)
        {
            // Skip some days (study gaps)
            if (studentRng.NextDouble() > sessionsPerDay / 2.0) continue;

            var dayTime = startDate.AddDays(day).AddHours(8 + studentRng.Next(12));
            int attemptsToday = (int)(sessionsPerDay * (2 + studentRng.Next(4)));

            for (int a = 0; a < attemptsToday && conceptOrder.Count > 0; a++)
            {
                var conceptIdx = Math.Min((int)(day * conceptOrder.Count / 60.0) + studentRng.Next(3), conceptOrder.Count - 1);
                var (path, _) = conceptOrder[conceptIdx];

                var isCorrect = studentRng.NextDouble() < accuracy + (day * 0.003); // slight improvement over time
                var attemptTime = dayTime.AddMinutes(a * 3 + studentRng.Next(5));
                var unixTs = attemptTime.ToUnixTimeSeconds();

                string action;
                if (isCorrect)
                {
                    // First time correct on this concept = A (add), subsequent = M (modify/mastery)
                    action = studentRng.NextDouble() < 0.3 ? "A" : "M";
                }
                else
                {
                    action = "D"; // Failed attempt
                }

                events.Add((unixTs, studentName, action, path, color));
            }
        }
    }
}

// Sort by timestamp and output
foreach (var (ts, user, action, path, color) in events.OrderBy(e => e.Timestamp))
{
    Console.WriteLine($"{ts}|{user}|{action}|/{path}|{color}");
}

Console.Error.WriteLine($"Generated {events.Count} events for {studentId} students across {allPaths.Count} concepts.");
