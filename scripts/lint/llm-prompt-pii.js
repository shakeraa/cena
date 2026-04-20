#!/usr/bin/env node
// =============================================================================
// Pre-commit / CI lint -- "No PII in LLM prompts" (ADR-0046, prr-022)
//
// This is the pre-commit first-line-of-defence. The xUnit architecture ratchet
// (NoPiiFieldInLlmPromptTest) is the authoritative build gate; this script
// mirrors the same banned-vocabulary regex so a developer catches the leak on
// their laptop in <1s without running `dotnet test`.
//
// Invariant (same as NoPiiFieldInLlmPromptTest):
//   Any .cs file under src/ that contains `[TaskRouting(` MUST NOT reference
//   any banned PII identifier listed in ADR-0046 Decision 1.
//
// Exit codes:
//   0 -- clean
//   1 -- one or more violations
//
// Flags:
//   --json          machine-readable output
//   --quiet         suppress the "no violations" success line
//
// Usage:
//   node scripts/lint/llm-prompt-pii.js
//   node scripts/lint/llm-prompt-pii.js --json
// =============================================================================

import { readFileSync, readdirSync, statSync } from "node:fs";
import { resolve, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = fileURLToPath(new URL(".", import.meta.url));
const ROOT = resolve(SCRIPT_DIR, "../..");

const args = process.argv.slice(2);
const JSON_OUT = args.includes("--json");
const QUIET = args.includes("--quiet");

// Must stay in sync with
// src/actors/Cena.Actors.Tests/Architecture/NoPiiFieldInLlmPromptTest.cs.
// When one changes, the other must change in the same PR -- ADR-0046 locks
// the pair as the single source of truth.
const BANNED = new RegExp(
  String.raw`(?<![A-Za-z0-9_])(?<name>` +
    String.raw`studentFullName[A-Za-z0-9_]*` +
    String.raw`|fullName[A-Za-z0-9_]*` +
    String.raw`|studentFirstName[A-Za-z0-9_]*` +
    String.raw`|studentLastName[A-Za-z0-9_]*` +
    String.raw`|studentSurname[A-Za-z0-9_]*` +
    String.raw`|studentEmail[A-Za-z0-9_]*` +
    String.raw`|studentEmailAddress[A-Za-z0-9_]*` +
    String.raw`|studentPhone[A-Za-z0-9_]*` +
    String.raw`|studentPhoneNumber[A-Za-z0-9_]*` +
    String.raw`|studentMobile[A-Za-z0-9_]*` +
    String.raw`|governmentId[A-Za-z0-9_]*` +
    String.raw`|israeliId[A-Za-z0-9_]*` +
    String.raw`|nationalId[A-Za-z0-9_]*` +
    String.raw`|teudatZehut[A-Za-z0-9_]*` +
    String.raw`|\bSSN\b` +
    String.raw`|birthDate[A-Za-z0-9_]*` +
    String.raw`|dateOfBirth[A-Za-z0-9_]*` +
    String.raw`|studentDob[A-Za-z0-9_]*` +
    String.raw`|homeAddress[A-Za-z0-9_]*` +
    String.raw`|streetAddress[A-Za-z0-9_]*` +
    String.raw`|postalAddress[A-Za-z0-9_]*` +
    String.raw`|parentName[A-Za-z0-9_]*` +
    String.raw`|parentEmail[A-Za-z0-9_]*` +
    String.raw`|parentPhone[A-Za-z0-9_]*` +
    String.raw`|guardianName[A-Za-z0-9_]*` +
    String.raw`|guardianEmail[A-Za-z0-9_]*` +
    String.raw`|guardianPhone[A-Za-z0-9_]*` +
    String.raw`|schoolName[A-Za-z0-9_]*` +
    String.raw`|schoolAddress[A-Za-z0-9_]*` +
    String.raw`)(?![A-Za-z0-9_])`,
  "gi",
);

const TASK_ROUTING = /\[TaskRouting\s*\(/;

// Attribute + scrubber declaration files are the canonical definitions of the
// banned vocabulary -- they reference the tokens as documentation, not as
// fields. Exempt them exactly as the xUnit ratchet does.
const SELF_REFERENTIAL = new Set([
  "src/shared/Cena.Infrastructure/Llm/TaskRoutingAttribute.cs".replaceAll("/", sep),
  "src/shared/Cena.Infrastructure/Llm/FeatureTagAttribute.cs".replaceAll("/", sep),
  "src/shared/Cena.Infrastructure/Llm/PiiPreScrubbedAttribute.cs".replaceAll("/", sep),
  "src/shared/Cena.Infrastructure/Llm/PiiPromptScrubber.cs".replaceAll("/", sep),
  "src/shared/Cena.Infrastructure/Llm/PiiPromptScrubberRegistration.cs".replaceAll("/", sep),
]);

function* walkCs(dir) {
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return;
  }
  for (const name of entries) {
    const full = resolve(dir, name);
    let st;
    try {
      st = statSync(full);
    } catch {
      continue;
    }
    if (st.isDirectory()) {
      if (
        name === "bin" ||
        name === "obj" ||
        name === "fixtures" ||
        name === "Tests"
      )
        continue;
      yield* walkCs(full);
      continue;
    }
    if (!name.endsWith(".cs")) continue;
    if (full.includes(`.Tests${sep}`)) continue;
    const rel = relative(ROOT, full);
    if (SELF_REFERENTIAL.has(rel)) continue;
    yield full;
  }
}

// Strip // line comments and "..." string literals so documentation and
// error-message copy referencing the banned tokens don't false-positive.
// Same heuristic as CostMetricEmittedTest's StripCommentsAndStrings.
function stripCommentsAndStrings(text) {
  text = text.replace(/\/\*[\s\S]*?\*\//g, "");
  const out = [];
  let inStr = false;
  let inLine = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inLine) {
      if (c === "\n") {
        inLine = false;
        out.push(c);
      }
      continue;
    }
    if (inStr) {
      if (c === '"' && (i === 0 || text[i - 1] !== "\\")) {
        inStr = false;
        out.push('"');
      }
      continue;
    }
    if (c === "/" && i + 1 < text.length && text[i + 1] === "/") {
      inLine = true;
      i++;
      continue;
    }
    if (c === '"') {
      inStr = true;
      out.push('"');
      continue;
    }
    out.push(c);
  }
  return out.join("");
}

function main() {
  const srcRoot = resolve(ROOT, "src");
  const violations = [];
  let scanned = 0;
  let withTaskRouting = 0;

  for (const file of walkCs(srcRoot)) {
    scanned++;
    const raw = readFileSync(file, "utf8");
    if (!TASK_ROUTING.test(raw)) continue;
    withTaskRouting++;
    const stripped = stripCommentsAndStrings(raw);
    const rel = relative(ROOT, file).replaceAll(sep, "/");

    const lines = stripped.split("\n");
    // Scan a window around each line to detect the "StudentPiiContext" scrubber-
    // configuration seam. A line that instantiates StudentPiiContext(...) spans
    // multiple source lines (C# named-parameter record construction), so a
    // line-local exemption isn't enough -- we need to check the preceding ~12
    // lines for a StudentPiiContext reference. That reference is the explicit
    // "I'm configuring the scrubber" marker and is not a leak.
    const SCRUBBER_CONFIG_CONTEXT_WINDOW = 12;
    for (let i = 0; i < lines.length; i++) {
      BANNED.lastIndex = 0;
      const m = BANNED.exec(lines[i]);
      if (!m) continue;

      // Check the preceding window for the scrubber-config marker.
      let inScrubberConfig = false;
      for (
        let j = Math.max(0, i - SCRUBBER_CONFIG_CONTEXT_WINDOW);
        j <= i;
        j++
      ) {
        if (lines[j].includes("StudentPiiContext")) {
          inScrubberConfig = true;
          break;
        }
      }
      if (inScrubberConfig) continue;

      violations.push({
        file: rel,
        line: i + 1,
        identifier: m.groups?.name ?? m[0],
      });
    }
  }

  if (JSON_OUT) {
    process.stdout.write(
      JSON.stringify(
        {
          ok: violations.length === 0,
          scanned,
          taskRoutingFiles: withTaskRouting,
          violations,
        },
        null,
        2,
      ) + "\n",
    );
  } else if (violations.length === 0) {
    if (!QUIET) {
      process.stdout.write(
        `ADR-0046 lint clean -- 0 banned-PII-identifier hits in ${withTaskRouting} [TaskRouting] file(s).\n`,
      );
    }
  } else {
    process.stderr.write(
      `ADR-0046 lint FAILED -- ${violations.length} banned-PII-identifier hit(s):\n\n`,
    );
    for (const v of violations) {
      process.stderr.write(
        `  ${v.file}:${v.line} -- identifier \`${v.identifier}\`\n`,
      );
    }
    process.stderr.write(
      "\nFix options (ADR-0046):\n" +
        "  (a) Replace the raw field with a structured placeholder\n" +
        "      (see Decision 2 -- {{student_pseudonym}}, {{age_band}}, etc.).\n" +
        "  (b) Remove the field from the prompt entirely.\n" +
        "  (c) Raise an ADR addendum if the ban is genuinely wrong for this\n" +
        "      seam -- do NOT silently add the file to an allowlist.\n",
    );
  }

  process.exit(violations.length === 0 ? 0 : 1);
}

main();
