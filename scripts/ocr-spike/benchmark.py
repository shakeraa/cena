"""Benchmark every registered OCR runner against the fixture set.

Iterates:  fixtures × runners  →  scored RecognitionResult rows.

Output:
    results/results.json    — full per-fixture per-runner payload
    results/results.md      — scored comparison table
    results/per_tool/*.json — individual raw outputs for debugging

The harness is tolerant: if a runner raises `RunnerUnavailable` at setup()
time the row is recorded as `skipped` with the exception message, so a
partial install still produces useful output.

Usage:
    python benchmark.py                    # full matrix (all runners × all fixtures)
    python benchmark.py --runners tesseract surya     # subset of runners
    python benchmark.py --fixtures student_photos     # subset of fixtures
    python benchmark.py --skip-heavy       # skip runners that need >2GB HF downloads
    python benchmark.py --dry-run          # don't actually run tools; print matrix
"""
from __future__ import annotations

import argparse
import importlib
import json
import sys
import time
import traceback
from dataclasses import asdict
from pathlib import Path
from typing import Any

# Relative imports so this also runs when invoked from repo root
sys.path.insert(0, str(Path(__file__).parent))

from metrics import (  # noqa: E402
    FixtureScore, SCORE_WEIGHTS, latency_to_score, math_equivalent,
    setup_to_score, throughput_to_score, wer,
)
from runners import RUNNER_MODULES, RunnerUnavailable, Runner  # noqa: E402

FIXTURE_ROOT = Path(__file__).parent / "fixtures"
GT_DIR = FIXTURE_ROOT / "ground_truth"
RESULTS_DIR = Path(__file__).parent / "results"
CACHE_DIR = Path(__file__).parent / "cache"


# ── Fixture loading ────────────────────────────────────────────────────────
def load_fixtures(subsets: list[str] | None) -> list[tuple[str, Path, dict]]:
    """Return list of (fixture_id, input_path, ground_truth_dict)."""
    fixtures: list[tuple[str, Path, dict]] = []

    wanted_subsets = set(subsets) if subsets else {"bagrut", "student_photos", "pdfs_mixed"}

    for subset in wanted_subsets:
        subset_dir = FIXTURE_ROOT / subset
        if not subset_dir.exists():
            print(f"  ⚠  no fixtures at {subset_dir}")
            continue
        for f in sorted(subset_dir.iterdir()):
            if f.suffix.lower() not in {".pdf", ".png", ".jpg", ".jpeg"}:
                continue
            fid = f"{subset}_{f.stem}"
            gt_path = GT_DIR / f"{fid}.json"
            if not gt_path.exists():
                # Fall back to a shorter id convention used by synthesize_student_photos
                alt = GT_DIR / f"{f.stem}.json"
                if alt.exists():
                    gt_path = alt
                else:
                    # Synthesize a minimal GT so the runner still executes;
                    # metrics will be mostly-zero but we still get latency/setup scores
                    gt = {"fixture_id": fid, "source": f.name, "stub": True}
                    fixtures.append((fid, f, gt))
                    continue
            gt = json.loads(gt_path.read_text(encoding="utf-8"))
            fixtures.append((fid, f, gt))

    return fixtures


# ── Runner loading ─────────────────────────────────────────────────────────
def load_runners(wanted: list[str] | None, skip_heavy: bool) -> list[Runner]:
    """Dynamically import every runner module; return instances."""
    runners: list[Runner] = []
    for mod_name in RUNNER_MODULES:
        short = mod_name.split(".")[-1].replace("_runner", "")
        if wanted and short not in wanted:
            continue
        try:
            mod = importlib.import_module(mod_name)
        except ModuleNotFoundError as e:
            print(f"  ⚠  {short}: module not importable ({e}) — skipping")
            continue
        except Exception as e:
            print(f"  ⚠  {short}: import error ({e}) — skipping")
            continue
        runner_cls = getattr(mod, "RUNNER", None) or getattr(mod, "Runner", None)
        if runner_cls is None:
            print(f"  ⚠  {short}: no RUNNER/Runner exported — skipping")
            continue
        instance = runner_cls()
        if skip_heavy and getattr(instance, "heavy", False):
            print(f"  ⚠  {short}: --skip-heavy flag → skipping")
            continue
        runners.append(instance)
    return runners


# ── Scoring ────────────────────────────────────────────────────────────────
def score_run(fid: str, runner_name: str, gt: dict, result: Any, setup_friction: int,
              surface: str) -> FixtureScore:
    # WER
    gt_text = ((gt.get("hebrew_text") or "") + "\n" + (gt.get("english_text") or "")).strip()
    pred_text = result.full_text if result else ""
    wer_val = wer(gt_text, pred_text) if gt_text else 0.0
    wer_score = max(0.0, 1.0 - wer_val)

    # Math equivalence — match each GT equation to the best candidate
    gt_eqs: list[str] = gt.get("latex_equations") or []
    pred_eqs: list[str] = result.all_latex if result else []
    if not gt_eqs:
        math_score = 1.0 if not pred_eqs else 0.5
    else:
        matched = 0
        used: set[int] = set()
        for e in gt_eqs:
            best_idx, best_ok = -1, False
            for i, p in enumerate(pred_eqs):
                if i in used:
                    continue
                ok, _strategy = math_equivalent(e, p)
                if ok:
                    best_idx, best_ok = i, True
                    break
            if best_ok:
                matched += 1
                used.add(best_idx)
        math_score = matched / max(1, len(gt_eqs))

    # Layout — from GT (manual) or auto (column count match). For now use 0.5 default.
    layout_manual = gt.get("layout", {}).get("manual_score")
    layout_score = (layout_manual / 3.0) if layout_manual is not None else 0.5

    # Figure recall
    gt_fig_count = gt.get("layout", {}).get("figures_count", 0)
    pred_fig_count = len(result.figures) if result else 0
    if gt_fig_count == 0:
        figure_recall = 1.0 if pred_fig_count == 0 else 0.7
    else:
        figure_recall = min(1.0, pred_fig_count / gt_fig_count)

    # Perf
    lat = result.latency_seconds if result else 10.0
    if surface == "A":
        perf_score = latency_to_score(lat, "A")
    else:
        # crude throughput estimate
        ppm = 60.0 / max(lat, 0.1)
        perf_score = throughput_to_score(ppm)

    setup_score = setup_to_score(setup_friction)

    fs = FixtureScore(
        fixture_id=fid,
        runner_name=runner_name,
        wer_score=wer_score,
        math_score=math_score,
        layout_score=layout_score,
        figure_recall=figure_recall,
        perf_score=perf_score,
        setup_score=setup_score,
    )
    fs.compute_weighted()
    return fs


# ── Main ───────────────────────────────────────────────────────────────────
def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--runners", nargs="*", default=None,
                    help="short names, e.g. tesseract surya pix2tex")
    ap.add_argument("--fixtures", nargs="*", default=None,
                    help="subsets: bagrut, student_photos, pdfs_mixed")
    ap.add_argument("--skip-heavy", action="store_true",
                    help="skip runners that need >2GB HF model downloads")
    ap.add_argument("--surface", choices=["A", "B"], default="A",
                    help="Surface A (student upload — latency-scored) or B (batch — throughput-scored)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    (RESULTS_DIR / "per_tool").mkdir(parents=True, exist_ok=True)

    fixtures = load_fixtures(args.fixtures)
    runners = load_runners(args.runners, args.skip_heavy)

    if not fixtures:
        print("ERROR: no fixtures found. Run bagrut_scrape / synthesize_student_photos / build_mixed_pdfs first.",
              file=sys.stderr)
        return 1
    if not runners:
        print("ERROR: no runners loaded. Install via ./setup.sh --all.", file=sys.stderr)
        return 1

    print(f"→ {len(fixtures)} fixtures × {len(runners)} runners = {len(fixtures) * len(runners)} runs")

    if args.dry_run:
        for r in runners:
            print(f"  runner: {r.name}")
        for fid, path, _gt in fixtures:
            print(f"  fixture: {fid}  ({path.name})")
        return 0

    all_results: list[dict] = []
    scores: list[FixtureScore] = []
    runner_status: dict[str, str] = {}

    for runner in runners:
        print(f"\n── {runner.name} ──")
        try:
            runner.setup(CACHE_DIR)
            runner_status[runner.name] = "available"
        except RunnerUnavailable as e:
            print(f"  SKIP ({e})")
            runner_status[runner.name] = f"unavailable: {e}"
            continue
        except Exception as e:
            print(f"  ERROR in setup: {e}")
            traceback.print_exc()
            runner_status[runner.name] = f"setup_error: {e}"
            continue

        for fid, path, gt in fixtures:
            t0 = time.perf_counter()
            try:
                result = runner.recognize(path)
                result.latency_seconds = time.perf_counter() - t0
                fs = score_run(fid, runner.name, gt, result, runner.setup_friction, args.surface)
                scores.append(fs)
                rec = result.to_dict()
                rec["score"] = asdict(fs)
                all_results.append(rec)
                print(f"  {fid:40s}  lat={result.latency_seconds:.2f}s  "
                      f"wer={fs.wer_score:.2f}  math={fs.math_score:.2f}  "
                      f"score={fs.weighted:.2f}")
            except Exception as e:
                print(f"  {fid:40s}  FAILED: {e}")
                all_results.append({
                    "runner": runner.name, "input": str(path), "error": str(e),
                    "score": None,
                })

    # Dump full output
    (RESULTS_DIR / "results.json").write_text(
        json.dumps(
            {
                "runner_status": runner_status,
                "results": all_results,
                "score_weights": SCORE_WEIGHTS,
            },
            indent=2, ensure_ascii=False,
        ),
        encoding="utf-8",
    )

    # Aggregate per-runner
    per_runner_totals: dict[str, list[float]] = {}
    for fs in scores:
        per_runner_totals.setdefault(fs.runner_name, []).append(fs.weighted)

    print("\n╭─ Per-runner averages ─────────────────────────────────────────")
    rows = sorted(per_runner_totals.items(),
                  key=lambda kv: sum(kv[1]) / len(kv[1]), reverse=True)
    for name, vals in rows:
        avg = sum(vals) / len(vals)
        print(f"│ {name:30s}  avg={avg:.3f}  n={len(vals)}")
    print("╰───────────────────────────────────────────────────────────────")

    # Write markdown summary
    md_lines = [
        "# OCR Spike — Benchmark Results",
        "",
        f"Ran {len(runners)} runners × {len(fixtures)} fixtures on Surface {args.surface}.",
        "",
        "| Runner | Avg score | N runs | Status |",
        "|--------|-----------|--------|--------|",
    ]
    for name, vals in rows:
        avg = sum(vals) / len(vals)
        md_lines.append(f"| {name} | {avg:.3f} | {len(vals)} | {runner_status.get(name, 'n/a')} |")

    md_lines += ["", "## Score weights", "",
                 "```json", json.dumps(SCORE_WEIGHTS, indent=2), "```", ""]

    (RESULTS_DIR / "results.md").write_text("\n".join(md_lines), encoding="utf-8")
    print(f"\n→ results.json and results.md written to {RESULTS_DIR.relative_to(Path.cwd())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
