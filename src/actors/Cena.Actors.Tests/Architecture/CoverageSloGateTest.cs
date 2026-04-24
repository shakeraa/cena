// =============================================================================
// Cena Platform — Coverage SLO Ship-gate Architecture Test (prr-210)
//
// Asserts the three load-bearing invariants of the coverage-SLO ship-gate:
//
//   (a) contracts/coverage/coverage-targets.yml exists at the repo root.
//   (b) Every active cell in that YAML has a resolvable target N > 0.
//       Either via an explicit 'min:' on the cell, a per-questionType
//       default, a per-methodology default, or the global default.
//   (c) The CI script scripts/shipgate/coverage-slo.mjs exits non-zero when
//       pointed at a crafted under-target fixture. This catches the
//       silently-passing regression where a refactor breaks the fail path.
//
// Additional guard-rails:
//   (d) The CI script also exits zero on the all-green fixture so we know
//       the fail path isn't accidentally firing on healthy input.
//   (e) The script file is executable text (not a binary) and starts with
//       the expected shebang — catches accidental overwrites.
//
// All assertions are framework-only (no SDK dependencies outside node, which
// runs the ship-gate on CI). If node is not on PATH we skip the process-
// executing subset of assertions but still validate YAML shape + file
// presence. That keeps the test green on slim/dev checkouts while the CI
// environment still enforces the hard invariant.
//
// See ops/slo/coverage-rung-slo.md for the policy.
// =============================================================================

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class CoverageSloGateTest
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) return dir.FullName;
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "actors", "Cena.Actors")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Repo root not found — looked for CLAUDE.md or src/actors/Cena.Actors/.");
    }

    private static string TargetsPath(string root) =>
        Path.Combine(root, "contracts", "coverage", "coverage-targets.yml");

    private static string ScriptPath(string root) =>
        Path.Combine(root, "scripts", "shipgate", "coverage-slo.mjs");

    private static string FixtureUnderPath(string root) =>
        Path.Combine(root, "scripts", "shipgate", "fixtures", "coverage-snapshot-under-target.json");

    private static string FixtureGreenPath(string root) =>
        Path.Combine(root, "scripts", "shipgate", "fixtures", "coverage-snapshot-all-green.json");

    private static string SlopDocPath(string root) =>
        Path.Combine(root, "ops", "slo", "coverage-rung-slo.md");

    // ── (a) YAML present ────────────────────────────────────────────────

    [Fact]
    public void TargetsFile_Exists()
    {
        var root = FindRepoRoot();
        var path = TargetsPath(root);
        Assert.True(
            File.Exists(path),
            $"prr-210: expected coverage SLO targets at {path}. " +
            "This file is the gate's single source of truth — deleting it silently disables the ship-gate.");
    }

    [Fact]
    public void SloDoc_Exists()
    {
        var root = FindRepoRoot();
        var doc = SlopDocPath(root);
        Assert.True(
            File.Exists(doc),
            $"prr-210: expected SLO policy doc at {doc}. Engineering decisions about N live here.");
    }

    // ── (b) every active cell resolves to a target > 0 ─────────────────

    [Fact]
    public void EveryActiveCell_ResolvesToRequiredN_GreaterThanZero()
    {
        var root = FindRepoRoot();
        var path = TargetsPath(root);
        Assert.True(File.Exists(path), $"missing {path}");

        var yaml = File.ReadAllText(path);
        var doc = ParseTargetsYaml(yaml);

        Assert.True(doc.Version >= 1, "targets yaml: missing or invalid version");
        Assert.True(doc.GlobalMin > 0, "targets yaml: defaults.global.min must be > 0 — else every cell silently passes");
        Assert.True(doc.Cells.Count > 0, "targets yaml: must declare at least one cell");

        var active = doc.Cells.Where(c => c.Active).ToList();
        Assert.True(active.Count > 0, "targets yaml: at least one cell must be active (the gate is pointless otherwise)");

        var violations = new List<string>();
        foreach (var cell in active)
        {
            var n = ResolveRequiredN(cell, doc);
            if (n <= 0)
                violations.Add(
                    $"  active cell {cell.Address} resolved to N={n} — either set 'min:' on the cell or add a default");
        }

        Assert.True(
            violations.Count == 0,
            "prr-210 invariant (b) violated — active cell(s) have no resolvable target N>0:\n" +
            string.Join("\n", violations));
    }

    // ── (c) script exits non-zero on crafted under-target fixture ──────

    [Fact]
    public void Script_ExitsNonZero_OnCraftedUnderTargetFixture()
    {
        var root = FindRepoRoot();
        var script = ScriptPath(root);
        Assert.True(File.Exists(script), $"missing script {script}");

        var fixture = FixtureUnderPath(root);
        Assert.True(File.Exists(fixture), $"missing fixture {fixture}");

        if (!NodeAvailable())
        {
            // Dev checkouts without node skip the process-executing half.
            // CI always has node.
            return;
        }

        var tmpReport = Path.Combine(
            Path.GetTempPath(),
            $"cena-prr210-arch-report-{Guid.NewGuid():N}.md");

        var (exit, stdout, stderr) = RunNode(
            root,
            script,
            "--targets", TargetsPath(root),
            "--snapshot", fixture,
            "--report", tmpReport);

        Assert.True(
            exit != 0,
            "prr-210 invariant (c) violated — the coverage-slo script MUST exit non-zero on the crafted under-target fixture. " +
            "If this test passes with exit=0, the fail path silently regressed.\n" +
            $"stdout:\n{stdout}\nstderr:\n{stderr}");

        // Report must be generated even on failure.
        Assert.True(
            File.Exists(tmpReport),
            "prr-210: coverage-slo script failed to write the status report on the fail path.");

        try { File.Delete(tmpReport); } catch { /* best effort */ }
    }

    // ── (d) script exits zero on the green fixture ─────────────────────

    [Fact]
    public void Script_ExitsZero_OnAllGreenFixture()
    {
        var root = FindRepoRoot();
        var script = ScriptPath(root);
        var fixture = FixtureGreenPath(root);
        Assert.True(File.Exists(fixture), $"missing fixture {fixture}");

        if (!NodeAvailable()) return;

        var tmpReport = Path.Combine(
            Path.GetTempPath(),
            $"cena-prr210-arch-green-{Guid.NewGuid():N}.md");

        var (exit, stdout, stderr) = RunNode(
            root,
            script,
            "--targets", TargetsPath(root),
            "--snapshot", fixture,
            "--report", tmpReport);

        Assert.True(
            exit == 0,
            "prr-210 invariant (d): the coverage-slo script MUST exit 0 on the all-green fixture. " +
            "If this fails the gate is firing spuriously on healthy input.\n" +
            $"stdout:\n{stdout}\nstderr:\n{stderr}");

        try { File.Delete(tmpReport); } catch { }
    }

    // ── (e) script is a valid node mjs file ────────────────────────────

    [Fact]
    public void Script_IsNodeMjsFile()
    {
        var root = FindRepoRoot();
        var script = ScriptPath(root);
        Assert.True(File.Exists(script), $"missing script {script}");

        var head = File.ReadAllText(script);
        Assert.StartsWith("#!/usr/bin/env node", head);
        Assert.Contains("coverage-targets.yml", head);
        Assert.Contains("process.exit", head);
    }

    // ── YAML parser (minimal, targeted to coverage-targets.yml shape) ──
    //
    // Mirrors the parser in scripts/shipgate/coverage-slo.mjs. Kept simple
    // on purpose — this test validates the contract file, not the parser.

    private sealed record TargetsDoc(
        int Version,
        int GlobalMin,
        IReadOnlyDictionary<string, int> MethodologyDefaults,
        IReadOnlyDictionary<string, int> QuestionTypeDefaults,
        IReadOnlyList<CellEntry> Cells);

    private sealed record CellEntry(
        string Topic,
        string Difficulty,
        string Methodology,
        string Track,
        string QuestionType,
        string Language,
        int? Min,
        bool Active)
    {
        public string Address =>
            $"{Track}/{Topic}/{Difficulty}/{Methodology}/{QuestionType}/{Language}";
    }

    private static TargetsDoc ParseTargetsYaml(string yaml)
    {
        // Line-based scanner: handles the narrow schema we own.
        var lines = yaml.Replace("\r\n", "\n").Split('\n');
        int version = 0;
        int globalMin = 0;
        var meth = new Dictionary<string, int>();
        var qt = new Dictionary<string, int>();
        var cells = new List<CellEntry>();

        string? section = null;
        CellBuilder? current = null;
        bool inDefaultsGlobal = false;
        bool inDefaultsMethodology = false;
        bool inDefaultsQuestionType = false;

        int Indent(string l) => l.Length - l.TrimStart(' ').Length;
        string Strip(string l)
        {
            // Remove comments.
            var idx = l.IndexOf('#');
            if (idx < 0) return l;
            // Preserve value if '#' was inside a quoted string — schema
            // doesn't use inline quotes so we keep this simple.
            return l.Substring(0, idx);
        }

        foreach (var raw in lines)
        {
            var line = Strip(raw);
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = Indent(line);
            var trimmed = line.Trim();

            if (indent == 0)
            {
                // Flush any in-progress cell.
                if (current is not null) { cells.Add(current.Build()); current = null; }
                section = null;
                inDefaultsGlobal = inDefaultsMethodology = inDefaultsQuestionType = false;

                var m = Regex.Match(trimmed, @"^([A-Za-z_][\w\-.]*):\s*(.*)$");
                if (!m.Success) continue;
                var key = m.Groups[1].Value;
                var val = m.Groups[2].Value.Trim();

                if (key == "version") version = int.Parse(val);
                else if (key == "defaults") section = "defaults";
                else if (key == "cells") section = "cells";
                continue;
            }

            if (section == "defaults")
            {
                if (indent == 2)
                {
                    inDefaultsGlobal = trimmed.StartsWith("global:");
                    inDefaultsMethodology = trimmed.StartsWith("methodology:");
                    inDefaultsQuestionType = trimmed.StartsWith("questionType:");
                    continue;
                }
                if (indent >= 4)
                {
                    var m = Regex.Match(trimmed, @"^([A-Za-z][\w\-]*):\s*(\d+)\s*$");
                    if (!m.Success) continue;
                    var key = m.Groups[1].Value;
                    var val = int.Parse(m.Groups[2].Value);
                    if (inDefaultsGlobal && key == "min") globalMin = val;
                    else if (inDefaultsMethodology) meth[key] = val;
                    else if (inDefaultsQuestionType) qt[key] = val;
                }
                continue;
            }

            if (section == "cells")
            {
                if (trimmed.StartsWith("- "))
                {
                    // New cell entry.
                    if (current is not null) cells.Add(current.Build());
                    current = new CellBuilder();
                    var rest = trimmed.Substring(2).Trim();
                    ApplyKv(current, rest);
                    continue;
                }
                if (current is not null)
                {
                    ApplyKv(current, trimmed);
                }
            }
        }

        if (current is not null) cells.Add(current.Build());

        return new TargetsDoc(version, globalMin, meth, qt, cells);
    }

    private static void ApplyKv(CellBuilder c, string kv)
    {
        var m = Regex.Match(kv, @"^([A-Za-z][\w\-]*):\s*(.*)$");
        if (!m.Success) return;
        var key = m.Groups[1].Value;
        var raw = m.Groups[2].Value.Trim();
        // Strip quotes if present.
        if ((raw.StartsWith("\"") && raw.EndsWith("\"")) ||
            (raw.StartsWith("'") && raw.EndsWith("'")))
        {
            raw = raw.Substring(1, raw.Length - 2);
        }
        switch (key)
        {
            case "topic": c.Topic = raw; break;
            case "difficulty": c.Difficulty = raw; break;
            case "methodology": c.Methodology = raw; break;
            case "track": c.Track = raw; break;
            case "questionType": c.QuestionType = raw; break;
            case "language": c.Language = raw; break;
            case "min": c.Min = int.Parse(raw); break;
            case "active": c.Active = raw == "true"; break;
            case "notes": /* ignored for this test */ break;
        }
    }

    private sealed class CellBuilder
    {
        public string Topic = "";
        public string Difficulty = "";
        public string Methodology = "";
        public string Track = "";
        public string QuestionType = "";
        public string Language = "en";
        public int? Min;
        public bool Active;
        public CellEntry Build() => new(
            Topic, Difficulty, Methodology, Track, QuestionType, Language, Min, Active);
    }

    private static int ResolveRequiredN(CellEntry cell, TargetsDoc doc)
    {
        if (cell.Min.HasValue) return cell.Min.Value;
        if (doc.QuestionTypeDefaults.TryGetValue(cell.QuestionType, out var qt)) return qt;
        if (doc.MethodologyDefaults.TryGetValue(cell.Methodology, out var m)) return m;
        return doc.GlobalMin;
    }

    // ── Process helpers ────────────────────────────────────────────────

    private static bool NodeAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("node", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static (int Exit, string Stdout, string Stderr) RunNode(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
        return (p.ExitCode, stdout, stderr);
    }
}
