// =============================================================================
// Cena Platform — Parametric Template Coverage CLI (prr-200, Strategy 1)
//
// Usage:
//   dotnet run --project src/tools/Cena.Tools.QuestionGen -- \
//     --template path/to/template.yaml \
//     --seed-start 0 --seed-count 1000 \
//     --target 500
//
// Prints one line per rung with the accepted/requested ratio plus the drop-
// reason histogram. The ship-gate in prr-210 consumes this output to enforce
// the per-rung variant-count SLO.
//
// This CLI uses an OFFLINE renderer (no SymPy sidecar). Production ingestion
// must go through the admin endpoint (TemplateGenerateEndpoint) which routes
// through ICasRouterService — the CLI is for coverage accounting, not for
// CAS-verified ingestion.
//
// The file is scanned by NoLlmInParametricPipelineTest — no LLM imports.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Tools.QuestionGen;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var opts = ParseArgs(args);
            if (opts is null)
            {
                PrintUsage();
                return 2;
            }

            var template = TemplateYamlLoader.LoadFromFile(opts.TemplatePath);
            template.Validate();

            ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            var compilerLogger = loggerFactory.CreateLogger<ParametricCompiler>();
            var renderer = new OfflineParametricRenderer();
            var compiler = new ParametricCompiler(renderer, compilerLogger);

            Console.Error.WriteLine($"[question-gen] template={template.Id} v{template.Version} " +
                $"subject={template.Subject} topic={template.Topic} " +
                $"track={template.Track} difficulty={template.Difficulty} " +
                $"methodology={template.Methodology}");
            Console.Error.WriteLine($"[question-gen] slot-space upper bound ≈ " +
                $"{ParametricCompiler.ComputeSlotSpaceUpperBound(template)}");

            // Walk the seed window in chunks, compiling up to `target`
            // variants from the cumulative window. For coverage accounting
            // we drive a single CompileAsync call with `count = target` and
            // let the compiler enumerate the attempt budget internally.
            ParametricCompileReport report;
            try
            {
                report = await compiler.CompileAsync(template, opts.SeedStart, opts.Target, CancellationToken.None);
            }
            catch (InsufficientSlotSpaceException ex)
            {
                Console.Error.WriteLine($"[question-gen] INSUFFICIENT template={template.Id} " +
                    $"produced={ex.Produced}/{ex.Requested} bound≈{ex.SlotSpaceUpperBound}");
                PrintReport(template, null, ex.Produced, ex.Requested);
                return 3;
            }

            PrintReport(template, report, report.AcceptedCount, opts.Target);
            return report.AcceptedCount >= opts.Target ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[question-gen] ERROR: {ex.Message}");
            return 1;
        }
    }

    private static void PrintReport(
        ParametricTemplate template,
        ParametricCompileReport? report,
        int accepted,
        int requested)
    {
        // Machine-readable one-liner for ship-gate consumption.
        // Format: rung=<id> track=<t> difficulty=<d> methodology=<m> accepted=<n>/<req> attempts=<a>
        var attempts = report?.TotalAttempts ?? 0;
        Console.WriteLine(
            $"rung={template.Id} track={template.Track} difficulty={template.Difficulty} " +
            $"methodology={template.Methodology} subject={template.Subject} topic={template.Topic} " +
            $"accepted={accepted}/{requested} attempts={attempts}");

        if (report is null) return;

        // Drop-reason histogram.
        var byKind = report.Drops
            .GroupBy(d => d.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count);
        foreach (var entry in byKind)
        {
            Console.WriteLine($"drop={entry.Kind} count={entry.Count}");
        }
    }

    private static CliOptions? ParseArgs(string[] args)
    {
        string? template = null;
        long seedStart = 0;
        int target = 100;
        long seedCount = 10_000;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--template":
                case "-t":
                    if (i + 1 >= args.Length) return null;
                    template = args[++i];
                    break;
                case "--seed-start":
                    if (i + 1 >= args.Length) return null;
                    if (!long.TryParse(args[++i], out seedStart)) return null;
                    break;
                case "--seed-count":
                    if (i + 1 >= args.Length) return null;
                    if (!long.TryParse(args[++i], out seedCount)) return null;
                    break;
                case "--target":
                    if (i + 1 >= args.Length) return null;
                    if (!int.TryParse(args[++i], out target)) return null;
                    break;
                case "--help":
                case "-h":
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}'");
                    return null;
            }
        }

        if (string.IsNullOrEmpty(template)) return null;
        if (!File.Exists(template))
        {
            Console.Error.WriteLine($"Template not found at '{template}'");
            return null;
        }

        return new CliOptions(template, seedStart, seedCount, target);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
Cena parametric template coverage CLI (prr-200)

Usage:
  dotnet run --project src/tools/Cena.Tools.QuestionGen -- \
    --template <path.yaml>      # parametric template YAML
    [--seed-start <long>]       # base seed (default 0)
    [--seed-count <long>]       # attempt-budget hint (default 10000)
    [--target <int>]            # required accepted count (default 100)

Exit codes:
  0  accepted >= target
  2  bad arguments
  3  insufficient slot space
  1  other error
""");
    }

    private sealed record CliOptions(string TemplatePath, long SeedStart, long SeedCount, int Target);
}
