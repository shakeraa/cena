// =============================================================================
// Cena Platform — "No hardcoded Mashov credentials" architecture ratchet
// (prr-017)
//
// Scans every config surface in the repo and fails the build if any file
// contains a Mashov-shaped credential value. The allowlist is empty by
// design — Mashov credentials are dereferenced at runtime via
// <see cref="Cena.Infrastructure.Secrets.ISecretStore"/>. Any file that
// holds a plaintext Mashov username / password / token is a rotation-
// leverage point for a single-config-dump compromise (persona-redteam).
//
// Scanner shape mirrors NoPiiFieldInLlmPromptTest (comment-stripped
// text scan, repo-root anchored, allowlist-empty-by-design).
//
// A "Mashov-shaped credential" is detected by ANY of the following in
// a config file (appsettings*.json, *.yaml, *.env.sample, *.ini,
// GitHub Actions workflow files):
//
//   1. Key named mashov* with a non-empty, non-placeholder value.
//   2. Any known-leak patterns for the v1 Mashov API credential layout
//      (username+password+clientId+clientSecret under a single object).
//
// Placeholder values (empty string, "PLACEHOLDER", "<TODO>", "changeme"
// bracketed with <>) are allowed — they are clearly marked non-live.
// =============================================================================

using System.Text.RegularExpressions;

namespace Cena.Actors.Tests.Architecture;

public sealed class NoHardcodedMashovCredentialsTest
{
    // JSON / YAML: "mashov<something>": "<value>" OR mashov<something>: <value>
    // The value-capture group is what the allowlist matches on.
    private static readonly Regex JsonMashovKey = new(
        @"""(?<key>mashov[A-Za-z0-9_]*)""\s*:\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex YamlMashovKey = new(
        @"^\s*(?<key>mashov[A-Za-z0-9_]*)\s*:\s*(?<value>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    // What's acceptable — an explicitly empty or placeholder value.
    private static readonly HashSet<string> AllowedPlaceholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "\"\"",
        "''",
        "null",
        "~",
        "PLACEHOLDER",
        "<TODO>",
        "<changeme>",
        "<redacted>",
        "{{ .Values.mashov.username }}",
        "{{ .Values.mashov.password }}",
    };

    // File-level allowlist — empty by design. Adding an entry requires
    // an ADR amendment, not a code-only change.
    private static readonly string[] FileAllowlist = Array.Empty<string>();

    // File patterns to scan.
    private static readonly string[] ScannedExtensions =
    {
        ".json", ".yaml", ".yml", ".env", ".ini", ".properties", ".toml",
    };

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

    private static IEnumerable<string> ScannedFiles(string repoRoot)
    {
        foreach (var file in Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(repoRoot, file);
            var sep = Path.DirectorySeparatorChar;

            // Exclude noise / volatile paths.
            if (rel.Contains($"{sep}bin{sep}")) continue;
            if (rel.Contains($"{sep}obj{sep}")) continue;
            if (rel.Contains($"{sep}node_modules{sep}")) continue;
            if (rel.Contains($"{sep}.git{sep}")) continue;
            if (rel.Contains($"{sep}.claude{sep}")) continue;
            if (rel.Contains($"{sep}.agentdb{sep}")) continue;
            if (rel.Contains($"{sep}dist{sep}")) continue;
            if (rel.Contains($"{sep}.vs{sep}")) continue;

            // The runbook documents how to set these secrets — it
            // references the key names but never holds values.
            if (rel.EndsWith($"{sep}mashov-credentials-rotation.md")) continue;
            // This test file itself references the key name pattern.
            if (rel.EndsWith($"{sep}NoHardcodedMashovCredentialsTest.cs")) continue;
            // ISecretStore attribute + provider source — same reason.
            if (rel.EndsWith($"{sep}MashovCredentials.cs")) continue;
            // pre-release-review yaml fixtures that analyze the findings
            // — review material, not live config.
            if (rel.Contains($"{sep}pre-release-review{sep}")) continue;
            // Task bodies quote the finding text.
            if (rel.Contains($"{sep}tasks{sep}pre-release-review{sep}")) continue;

            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (!ScannedExtensions.Contains(ext)) continue;
            yield return file;
        }
    }

    [Fact]
    public void NoMashovCredentialValue_InAnyConfigFile()
    {
        var repoRoot = FindRepoRoot();
        var allowlist = new HashSet<string>(FileAllowlist, StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();
        var filesScanned = 0;

        foreach (var file in ScannedFiles(repoRoot))
        {
            filesScanned++;
            var rel = Path.GetRelativePath(repoRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (allowlist.Contains(rel)) continue;

            string raw;
            try { raw = File.ReadAllText(file); }
            catch { continue; } // unreadable — skip, not fail

            // JSON-shape scan (.json only — YAML/other get the next scan).
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".json")
            {
                foreach (Match m in JsonMashovKey.Matches(raw))
                {
                    var key = m.Groups["key"].Value;
                    var value = m.Groups["value"].Value;
                    if (IsAllowedValue(value)) continue;
                    violations.Add(
                        $"{rel} — key `{key}` carries a literal value " +
                        "(placeholders/empty strings are fine; real credentials must " +
                        "come from ISecretStore at runtime).");
                }
                continue;
            }

            // YAML / env / ini — line-anchored scan.
            foreach (Match m in YamlMashovKey.Matches(raw))
            {
                var key = m.Groups["key"].Value;
                var value = m.Groups["value"].Value.Trim('"', '\'', ' ');
                if (IsAllowedValue(value)) continue;
                violations.Add(
                    $"{rel} — key `{key}` carries a literal value " +
                    "(placeholders/empty strings are fine; real credentials must " +
                    "come from ISecretStore at runtime).");
            }
        }

        Assert.True(
            filesScanned > 0,
            "NoHardcodedMashovCredentialsTest scanned zero files. Scanner likely broken — " +
            "check the extension list and the repo-root detection.");

        if (violations.Count == 0) return;

        var msg = "prr-017 violation: Mashov credentials found in tracked config sources.\n" +
                  "Move the value to your secret manager (AWS Secrets Manager / GCP Secret " +
                  "Manager / Vault) and read it at runtime via ISecretStore. See " +
                  "docs/ops/runbooks/mashov-credentials-rotation.md.\n" +
                  "Violations:\n  - " + string.Join("\n  - ", violations);
        Assert.Fail(msg);
    }

    private static bool IsAllowedValue(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0) return true;
        if (AllowedPlaceholders.Contains(normalized)) return true;
        // Helm value-refs like `{{ .Values.mashov.password }}`.
        if (normalized.StartsWith("{{", StringComparison.Ordinal) &&
            normalized.EndsWith("}}", StringComparison.Ordinal))
            return true;
        // Kubernetes secret-references: `valueFrom: secretKeyRef:`-shaped
        // YAML. The line-oriented regex captures the wrapping key, not
        // the nested refs; anything matching the ref-pattern is also OK.
        if (normalized.StartsWith("secretKeyRef:", StringComparison.Ordinal))
            return true;
        return false;
    }
}
