#!/usr/bin/env python3
"""
RDY-024: BKT Parameter Calibration via EM Algorithm
====================================================

Phase A: Data export from Marten/PostgreSQL event store.
Phase B: Expectation-Maximization parameter estimation per subject.

Usage:
  # Export pilot data to CSV
  python scripts/bkt-calibration.py export --pg-dsn "host=localhost port=5433 dbname=cena user=cena password=cena_dev_password"

  # Run EM calibration on exported data
  python scripts/bkt-calibration.py calibrate --input data/pilot-attempts.csv --output config/bkt-params.json

  # Validate calibrated params produce better predictions
  python scripts/bkt-calibration.py validate --input data/pilot-attempts.csv --params config/bkt-params.json

Requirements:
  pip install psycopg2-binary pandas numpy

References:
  Corbett & Anderson (1994). Knowledge Tracing: Modeling the Acquisition of Procedural Knowledge.
  Baker et al. (2008). More Accurate Student Modeling through Contextual Estimation of Slip and Guess.
"""

import argparse
import json
import sys
from pathlib import Path

import numpy as np
import pandas as pd


# =============================================================================
# DATA EXPORT (Phase A)
# =============================================================================

EXPORT_SQL = """
SELECT
    e.data->>'StudentId'                           AS student_id,
    e.data->>'ConceptId'                           AS concept_id,
    e.data->>'SessionId'                           AS session_id,
    (e.data->>'IsCorrect')::boolean                AS is_correct,
    (e.data->>'ResponseTimeMs')::int               AS response_time_ms,
    (e.data->>'HintCountUsed')::int                AS hints_used,
    e.data->>'QuestionType'                        AS question_type,
    e.timestamp                                    AS attempted_at,
    -- Subject resolved from concept hierarchy (join with curriculum graph)
    COALESCE(e.data->>'Subject', 'unknown')        AS subject
FROM mt_events e
WHERE e.type IN (
    'concept_attempted_v1',
    'concept_attempted_v2',
    'concept_attempted_v3'
)
ORDER BY e.data->>'StudentId', e.timestamp;
"""


def export_data(pg_dsn: str, output: str) -> None:
    """Export concept attempts from PostgreSQL to CSV."""
    try:
        import psycopg2
    except ImportError:
        print("ERROR: psycopg2 not installed. Run: pip install psycopg2-binary", file=sys.stderr)
        sys.exit(1)

    print(f"Connecting to PostgreSQL...")
    conn = psycopg2.connect(pg_dsn)
    try:
        df = pd.read_sql(EXPORT_SQL, conn)
        out_path = Path(output)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        df.to_csv(out_path, index=False)
        print(f"Exported {len(df)} attempts to {out_path}")
        print(f"Subjects found: {df['subject'].nunique()}")
        print(f"Students: {df['student_id'].nunique()}")
        print(f"Concepts: {df['concept_id'].nunique()}")

        # Check minimum sample size per concept
        concept_counts = df.groupby('concept_id').size()
        sparse = (concept_counts < 200).sum()
        if sparse > 0:
            print(f"WARNING: {sparse} concepts have <200 attempts (unstable estimates)")
    finally:
        conn.close()


# =============================================================================
# EM CALIBRATION (Phase B)
# =============================================================================

# Default BKT parameters (match BktParameters.Default in C#)
DEFAULTS = {
    "pLearning": 0.10,
    "pSlip": 0.05,
    "pGuess": 0.20,
    "pForget": 0.02,
    "pInitial": 0.10,
}

# Parameter bounds to prevent degenerate estimates
BOUNDS = {
    "pLearning": (0.01, 0.50),
    "pSlip": (0.01, 0.40),
    "pGuess": (0.01, 0.40),
    "pForget": (0.001, 0.20),
    "pInitial": (0.01, 0.50),
}

MAX_EM_ITERATIONS = 200
CONVERGENCE_THRESHOLD = 1e-6


def em_bkt(sequences: list[list[bool]], params: dict | None = None) -> dict:
    """
    Estimate BKT parameters via Expectation-Maximization.

    Args:
        sequences: List of student attempt sequences (list of bool per student-concept).
        params: Initial parameter estimates. Defaults to DEFAULTS.

    Returns:
        Dict with estimated pLearning, pSlip, pGuess, pForget, pInitial.
    """
    if params is None:
        params = dict(DEFAULTS)

    p_l = params["pLearning"]
    p_s = params["pSlip"]
    p_g = params["pGuess"]
    p_f = params["pForget"]
    p_0 = params["pInitial"]

    for iteration in range(MAX_EM_ITERATIONS):
        # Accumulators for M-step
        num_learn = 0.0
        den_learn = 0.0
        num_slip = 0.0
        den_slip_correct = 0.0
        num_guess = 0.0
        den_guess_incorrect = 0.0
        num_forget = 0.0
        den_forget = 0.0
        num_initial = 0.0
        den_initial = 0.0

        for seq in sequences:
            if len(seq) == 0:
                continue

            # Forward pass: compute P(L_t | obs_1..t) for each timestep
            p_known = p_0
            posteriors = []

            for obs in seq:
                # P(correct) given current state
                p_correct = p_known * (1 - p_s) + (1 - p_known) * p_g

                # Posterior P(L | obs) via Bayes
                if obs:  # correct
                    p_known_post = p_known * (1 - p_s) / max(p_correct, 1e-10)
                else:  # incorrect
                    p_known_post = p_known * p_s / max(1 - p_correct, 1e-10)

                posteriors.append(p_known_post)

                # Transition: learning + forgetting
                p_known = p_known_post + (1 - p_known_post) * p_l
                p_known = p_known * (1 - p_f)
                p_known = np.clip(p_known, 0.01, 0.99)

            # Accumulate sufficient statistics
            p_k = p_0
            for t, obs in enumerate(seq):
                post = posteriors[t]

                # Learning transition stats
                if t > 0:
                    prev_post = posteriors[t - 1]
                    # P(not known at t-1, known at t) ≈ P(T)
                    num_learn += (1 - prev_post) * p_l
                    den_learn += (1 - prev_post)

                    # Forgetting stats
                    num_forget += prev_post * p_f
                    den_forget += prev_post

                # Slip/Guess stats
                if obs:  # correct
                    num_slip += post * p_s  # slipped but was known
                    den_slip_correct += post
                    num_guess += (1 - post) * p_g
                else:  # incorrect
                    num_slip += post * p_s
                    den_slip_correct += post

                if not obs:
                    den_guess_incorrect += (1 - post)

            # Initial knowledge
            num_initial += posteriors[0] if posteriors else p_0
            den_initial += 1.0

        # M-step: update parameters
        new_p_l = num_learn / max(den_learn, 1e-10)
        new_p_s = num_slip / max(den_slip_correct, 1e-10)
        new_p_g = num_guess / max(den_guess_incorrect, 1e-10)
        new_p_f = num_forget / max(den_forget, 1e-10)
        new_p_0 = num_initial / max(den_initial, 1e-10)

        # Clamp to bounds
        new_p_l = np.clip(new_p_l, *BOUNDS["pLearning"])
        new_p_s = np.clip(new_p_s, *BOUNDS["pSlip"])
        new_p_g = np.clip(new_p_g, *BOUNDS["pGuess"])
        new_p_f = np.clip(new_p_f, *BOUNDS["pForget"])
        new_p_0 = np.clip(new_p_0, *BOUNDS["pInitial"])

        # Check convergence
        delta = (
            abs(new_p_l - p_l) + abs(new_p_s - p_s) +
            abs(new_p_g - p_g) + abs(new_p_f - p_f) + abs(new_p_0 - p_0)
        )

        p_l, p_s, p_g, p_f, p_0 = new_p_l, new_p_s, new_p_g, new_p_f, new_p_0

        if delta < CONVERGENCE_THRESHOLD:
            print(f"  EM converged after {iteration + 1} iterations (delta={delta:.2e})")
            break
    else:
        print(f"  EM did not converge after {MAX_EM_ITERATIONS} iterations (delta={delta:.2e})")

    return {
        "pLearning": round(float(p_l), 6),
        "pSlip": round(float(p_s), 6),
        "pGuess": round(float(p_g), 6),
        "pForget": round(float(p_f), 6),
        "pInitial": round(float(p_0), 6),
    }


def calibrate(input_csv: str, output_json: str) -> None:
    """Run EM calibration per subject and write config/bkt-params.json."""
    df = pd.read_csv(input_csv)
    print(f"Loaded {len(df)} attempts from {input_csv}")

    subjects = df["subject"].unique()
    print(f"Calibrating {len(subjects)} subjects: {', '.join(sorted(subjects))}")

    result = {
        "BktCalibration": {
            "version": 2,
            "calibratedAt": pd.Timestamp.utcnow().isoformat(),
            "calibrationSource": f"EM from {input_csv} ({len(df)} attempts)",
            "defaults": dict(DEFAULTS),
            "subjects": {},
        },
        "FeatureFlags": {
            "BktCalibratedParams": False,
        },
    }

    for subject in sorted(subjects):
        sub_df = df[df["subject"] == subject]
        print(f"\n--- {subject} ({len(sub_df)} attempts) ---")

        if len(sub_df) < 100:
            print(f"  SKIP: <100 attempts, using defaults")
            result["BktCalibration"]["subjects"][subject] = dict(DEFAULTS)
            continue

        # Build per-student-concept sequences
        sequences = []
        for (sid, cid), group in sub_df.groupby(["student_id", "concept_id"]):
            seq = group.sort_values("attempted_at")["is_correct"].tolist()
            if len(seq) >= 3:  # need at least 3 observations
                sequences.append(seq)

        if len(sequences) < 10:
            print(f"  SKIP: <10 valid sequences, using defaults")
            result["BktCalibration"]["subjects"][subject] = dict(DEFAULTS)
            continue

        print(f"  {len(sequences)} sequences")
        params = em_bkt(sequences)
        result["BktCalibration"]["subjects"][subject] = params
        print(f"  Result: {params}")

    out_path = Path(output_json)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w") as f:
        json.dump(result, f, indent=2)
    print(f"\nWrote calibrated params to {out_path}")
    print("NOTE: Set FeatureFlags.BktCalibratedParams=true to activate.")


# =============================================================================
# VALIDATION (Phase B)
# =============================================================================

def validate(input_csv: str, params_json: str) -> None:
    """Compare prediction accuracy: default params vs calibrated params."""
    df = pd.read_csv(input_csv)
    with open(params_json) as f:
        config = json.load(f)

    cal = config.get("BktCalibration", config)
    subjects_params = cal.get("subjects", {})
    defaults = cal.get("defaults", DEFAULTS)

    print(f"Validating on {len(df)} attempts...")
    print(f"{'Subject':<15} {'Default AUC':<14} {'Calibrated AUC':<16} {'Improvement'}")
    print("-" * 60)

    for subject in sorted(df["subject"].unique()):
        sub_df = df[df["subject"] == subject]
        params = subjects_params.get(subject, defaults)

        default_acc = _predict_accuracy(sub_df, DEFAULTS)
        calibrated_acc = _predict_accuracy(sub_df, params)
        improvement = calibrated_acc - default_acc

        marker = "+" if improvement > 0 else ""
        print(f"{subject:<15} {default_acc:<14.4f} {calibrated_acc:<16.4f} {marker}{improvement:.4f}")


def _predict_accuracy(df: pd.DataFrame, params: dict) -> float:
    """Compute prediction accuracy using given BKT parameters."""
    p_l = params["pLearning"]
    p_s = params["pSlip"]
    p_g = params["pGuess"]
    p_f = params["pForget"]
    p_0 = params["pInitial"]

    correct_predictions = 0
    total = 0

    for (sid, cid), group in df.groupby(["student_id", "concept_id"]):
        seq = group.sort_values("attempted_at")["is_correct"].tolist()
        p_known = p_0

        for obs in seq:
            p_correct = p_known * (1 - p_s) + (1 - p_known) * p_g
            predicted = p_correct >= 0.5

            if predicted == obs:
                correct_predictions += 1
            total += 1

            # Update
            if obs:
                p_known_post = p_known * (1 - p_s) / max(p_correct, 1e-10)
            else:
                p_known_post = p_known * p_s / max(1 - p_correct, 1e-10)

            p_known = p_known_post + (1 - p_known_post) * p_l
            p_known = p_known * (1 - p_f)
            p_known = np.clip(p_known, 0.01, 0.99)

    return correct_predictions / max(total, 1)


# =============================================================================
# CLI
# =============================================================================

def main():
    parser = argparse.ArgumentParser(description="BKT Parameter Calibration (RDY-024)")
    sub = parser.add_subparsers(dest="command", required=True)

    # export
    exp = sub.add_parser("export", help="Export pilot data from PostgreSQL")
    exp.add_argument("--pg-dsn", required=True, help="PostgreSQL connection string")
    exp.add_argument("--output", default="data/pilot-attempts.csv", help="Output CSV path")

    # calibrate
    cal = sub.add_parser("calibrate", help="Run EM calibration on exported data")
    cal.add_argument("--input", required=True, help="Input CSV from export step")
    cal.add_argument("--output", default="config/bkt-params.json", help="Output JSON config")

    # validate
    val = sub.add_parser("validate", help="Compare default vs calibrated accuracy")
    val.add_argument("--input", required=True, help="Input CSV from export step")
    val.add_argument("--params", required=True, help="Calibrated params JSON")

    args = parser.parse_args()

    if args.command == "export":
        export_data(args.pg_dsn, args.output)
    elif args.command == "calibrate":
        calibrate(args.input, args.output)
    elif args.command == "validate":
        validate(args.input, args.params)


if __name__ == "__main__":
    main()
