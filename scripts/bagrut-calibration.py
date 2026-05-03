#!/usr/bin/env python3
"""
RDY-028: Bagrut Anchor Calibration Pipeline
============================================

1. Load anchor items from config/bagrut-anchors.json
2. Validate pass rate → IRT difficulty mapping (b = -logit(p))
3. Run concurrent calibration: calibrate non-anchor items relative to anchors
4. Validate Easy/Medium/Hard bands against Bagrut difficulty distribution
5. Output updated difficulty parameters for the item bank

Usage:
  python scripts/bagrut-calibration.py validate-anchors
  python scripts/bagrut-calibration.py calibrate --pilot-data data/pilot-attempts.csv
  python scripts/bagrut-calibration.py validate-bands
  python scripts/bagrut-calibration.py report

Requirements:
  pip install numpy pandas

References:
  Kolen & Brennan (2014). Test Equating, Scaling, and Linking.
  Wright & Stone (1979). Best Test Design — Rasch anchor calibration.
"""

import argparse
import json
import sys
from pathlib import Path

import numpy as np

ANCHORS_PATH = Path(__file__).parent / "../config/bagrut-anchors.json"


def load_anchors() -> dict:
    with open(ANCHORS_PATH) as f:
        return json.load(f)


def pass_rate_to_difficulty(p: float) -> float:
    """Convert pass rate to IRT difficulty (Rasch logit): b = -ln(p/(1-p))."""
    p = np.clip(p, 0.01, 0.99)
    return -np.log(p / (1 - p))


def difficulty_to_elo(b: float, center: float = 1500, scale: float = 200) -> float:
    """Convert IRT difficulty (logit) to Elo rating."""
    return b * scale + center


def elo_to_difficulty(elo: float, center: float = 1500, scale: float = 200) -> float:
    """Convert Elo rating to IRT difficulty (logit)."""
    return (elo - center) / scale


# ── Validate Anchors ──────────────────────────────────────────────────

def validate_anchors():
    """Verify pass rate → difficulty mapping is internally consistent."""
    data = load_anchors()
    errors = 0

    for track_id, track in data["tracks"].items():
        print(f"\n=== {track_id} ({len(track['anchors'])} anchors) ===")
        print(f"{'ID':<25} {'Pass%':>6} {'Expected b':>10} {'Stored b':>10} {'Delta':>8} {'Elo':>6} {'Band'}")
        print("-" * 90)

        for anchor in track["anchors"]:
            p = anchor["passRate"]
            expected_b = pass_rate_to_difficulty(p)
            stored_b = anchor["difficulty"]
            delta = abs(expected_b - stored_b)
            elo = difficulty_to_elo(stored_b)

            flag = "  MISMATCH" if delta > 0.05 else ""
            if delta > 0.05:
                errors += 1

            print(f"{anchor['anchorId']:<25} {p*100:5.1f}% {expected_b:10.2f} {stored_b:10.2f} {delta:8.3f} {elo:6.0f} {anchor['band']}{flag}")

    # Validate band thresholds
    bands = data["bandThresholds"]
    print(f"\n=== Band Thresholds ===")
    print(f"  Easy:   b < {bands['easy']['max']}")
    print(f"  Medium: {bands['medium']['min']} <= b <= {bands['medium']['max']}")
    print(f"  Hard:   b > {bands['hard']['min']}")

    # Verify each anchor's band assignment matches thresholds
    band_errors = 0
    for track_id, track in data["tracks"].items():
        for anchor in track["anchors"]:
            b = anchor["difficulty"]
            expected_band = (
                "easy" if b < bands["easy"]["max"]
                else "hard" if b > bands["hard"]["min"]
                else "medium"
            )
            if expected_band != anchor["band"]:
                print(f"  BAND MISMATCH: {anchor['anchorId']} b={b:.2f} stored={anchor['band']} expected={expected_band}")
                band_errors += 1

    if errors == 0 and band_errors == 0:
        print(f"\nVALIDATION PASSED: all difficulties match pass rates, all bands consistent")
    else:
        print(f"\nVALIDATION FAILED: {errors} difficulty mismatches, {band_errors} band mismatches")
        sys.exit(1)


# ── Concurrent Calibration ────────────────────────────────────────────

def calibrate(pilot_csv: str):
    """
    Run concurrent calibration: anchor items fix the scale, non-anchor items
    are calibrated relative to anchors using Rasch logit estimation.
    """
    try:
        import pandas as pd
    except ImportError:
        print("ERROR: pandas not installed. Run: pip install pandas", file=sys.stderr)
        sys.exit(1)

    data = load_anchors()
    df = pd.read_csv(pilot_csv)
    print(f"Loaded {len(df)} responses from {pilot_csv}")

    # Build anchor difficulty map
    anchor_map = {}
    for track_id, track in data["tracks"].items():
        for anchor in track["anchors"]:
            anchor_map[anchor["conceptId"]] = anchor["difficulty"]

    print(f"Anchor items: {len(anchor_map)}")

    # Group by question, compute Rasch difficulty
    by_question = df.groupby("question_id").agg(
        concept_id=("concept_id", "first"),
        n=("is_correct", "count"),
        p_correct=("is_correct", "mean")
    ).reset_index()

    by_question["p_correct"] = by_question["p_correct"].clip(0.01, 0.99)
    by_question["raw_difficulty"] = -np.log(by_question["p_correct"] / (1 - by_question["p_correct"]))

    # Compute anchor scale shift: mean(anchor_raw - anchor_stored)
    anchor_rows = by_question[by_question["concept_id"].isin(anchor_map)]
    if len(anchor_rows) < 3:
        print(f"WARNING: only {len(anchor_rows)} anchor items found in pilot data (need >= 3)")
        print("Calibration unreliable — using raw difficulties without anchor adjustment")
        shift = 0.0
    else:
        anchor_rows = anchor_rows.copy()
        anchor_rows["stored_b"] = anchor_rows["concept_id"].map(anchor_map)
        shift = (anchor_rows["raw_difficulty"] - anchor_rows["stored_b"]).mean()
        print(f"Anchor scale shift: {shift:.3f} (subtracted from raw difficulties)")

    # Apply shift: calibrated_b = raw_b - shift
    by_question["calibrated_difficulty"] = by_question["raw_difficulty"] - shift
    by_question["elo"] = by_question["calibrated_difficulty"].apply(difficulty_to_elo)
    by_question["is_anchor"] = by_question["concept_id"].isin(anchor_map)

    # Assign bands
    bands = data["bandThresholds"]
    by_question["band"] = by_question["calibrated_difficulty"].apply(
        lambda b: "easy" if b < bands["easy"]["max"]
        else "hard" if b > bands["hard"]["min"]
        else "medium"
    )

    # Report
    print(f"\n{'QuestionID':<20} {'Concept':<10} {'N':>5} {'P%':>6} {'Raw b':>7} {'Cal b':>7} {'Elo':>6} {'Band':<7} {'Anchor'}")
    print("-" * 90)
    for _, row in by_question.sort_values("calibrated_difficulty").iterrows():
        flag = "*" if row["is_anchor"] else " "
        print(f"{row['question_id']:<20} {row['concept_id']:<10} {row['n']:5d} {row['p_correct']*100:5.1f}% {row['raw_difficulty']:7.2f} {row['calibrated_difficulty']:7.2f} {row['elo']:6.0f} {row['band']:<7} {flag}")

    # Band distribution
    print(f"\nBand distribution:")
    for band in ["easy", "medium", "hard"]:
        count = (by_question["band"] == band).sum()
        pct = count / len(by_question) * 100
        print(f"  {band:<8} {count:3d} items ({pct:.0f}%)")

    # Output calibrated params
    output = {
        "calibratedAt": pd.Timestamp.utcnow().isoformat(),
        "anchorShift": round(float(shift), 4),
        "items": [
            {
                "questionId": row["question_id"],
                "conceptId": row["concept_id"],
                "difficulty": round(float(row["calibrated_difficulty"]), 4),
                "elo": round(float(row["elo"]), 0),
                "band": row["band"],
                "responseCount": int(row["n"]),
                "isAnchor": bool(row["is_anchor"])
            }
            for _, row in by_question.iterrows()
        ]
    }

    out_path = Path("data/bagrut-calibrated-items.json")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w") as f:
        json.dump(output, f, indent=2)
    print(f"\nWrote calibrated params to {out_path}")


# ── Validate Bands ────────────────────────────────────────────────────

def validate_bands():
    """Check that anchor distribution across bands is reasonable."""
    data = load_anchors()
    bands = data["bandThresholds"]

    for track_id, track in data["tracks"].items():
        anchors = track["anchors"]
        easy = [a for a in anchors if a["band"] == "easy"]
        medium = [a for a in anchors if a["band"] == "medium"]
        hard = [a for a in anchors if a["band"] == "hard"]

        print(f"\n{track_id}: {len(anchors)} anchors — {len(easy)} easy, {len(medium)} medium, {len(hard)} hard")

        if len(easy) == 0 or len(hard) == 0:
            print(f"  WARNING: missing easy or hard anchors — band boundaries may be unreliable")

        # Difficulty range
        diffs = [a["difficulty"] for a in anchors]
        print(f"  Difficulty range: [{min(diffs):.2f}, {max(diffs):.2f}]")
        print(f"  Mean difficulty: {np.mean(diffs):.2f}")
        print(f"  Band thresholds: easy < {bands['easy']['max']}, hard > {bands['hard']['min']}")


# ── Report ────────────────────────────────────────────────────────────

def report():
    """Generate a summary report of the anchor calibration baseline."""
    data = load_anchors()

    total_anchors = sum(len(t["anchors"]) for t in data["tracks"].values())
    print(f"=== Bagrut Calibration Baseline Report ===\n")
    print(f"Total anchor items: {total_anchors}")
    print(f"Tracks: {', '.join(data['tracks'].keys())}")

    pv = data["predictiveValidity"]
    print(f"\nPredictive validity metric:")
    print(f"  {pv['metric']}")
    print(f"  Baseline target: {pv['baselineTarget']*100:.0f}% accuracy")
    print(f"  Required sample: N={pv['requiredSampleSize']} students")

    validate_anchors()
    validate_bands()


# ── CLI ───────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Bagrut Anchor Calibration (RDY-028)")
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("validate-anchors", help="Validate pass rate → difficulty mapping")
    sub.add_parser("validate-bands", help="Validate difficulty band distribution")
    sub.add_parser("report", help="Full calibration baseline report")

    cal = sub.add_parser("calibrate", help="Concurrent calibration from pilot data")
    cal.add_argument("--pilot-data", required=True, help="Pilot data CSV")

    args = parser.parse_args()

    if args.command == "validate-anchors":
        validate_anchors()
    elif args.command == "validate-bands":
        validate_bands()
    elif args.command == "report":
        report()
    elif args.command == "calibrate":
        calibrate(args.pilot_data)


if __name__ == "__main__":
    main()
