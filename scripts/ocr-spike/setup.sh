#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# OCR Spike bootstrap — creates .venv, installs core deps.
# Full runner deps are opt-in via `./setup.sh --all`.
#
# Re-runnable: skips steps whose artifacts already exist.
# -----------------------------------------------------------------------------
set -euo pipefail

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "${SCRIPT_DIR}"

MODE="core"
if [[ "${1:-}" == "--all" ]]; then
  MODE="all"
fi

# 1 — Python version check
PY_MIN="3.10"
PY_VER=$(python3 -c 'import sys; print(f"{sys.version_info.major}.{sys.version_info.minor}")')
if [[ "$(printf '%s\n%s\n' "$PY_MIN" "$PY_VER" | sort -V | head -1)" != "$PY_MIN" ]]; then
  echo "ERROR: Python $PY_MIN+ required (found $PY_VER)" >&2
  exit 1
fi

# 2 — arch check (warn if x86_64 under Rosetta)
ARCH=$(python3 -c 'import platform; print(platform.machine())')
if [[ "$ARCH" != "arm64" ]] && [[ "$(uname)" == "Darwin" ]]; then
  echo "WARNING: Python arch is $ARCH on macOS. Native arm64 recommended for MPS." >&2
fi

# 3 — venv
if [[ ! -d .venv ]]; then
  echo "→ Creating .venv"
  python3 -m venv .venv
fi

# 4 — activate + upgrade
# shellcheck disable=SC1091
source .venv/bin/activate
python -m pip install --upgrade pip wheel setuptools >/dev/null

# 5 — install
echo "→ Installing requirements-core.txt"
pip install -r requirements-core.txt

if [[ "$MODE" == "all" ]]; then
  echo "→ Installing requirements.txt (full runner set — may take a while)"
  pip install -r requirements.txt || {
    echo "One or more heavy deps failed. Inspect above; runners that failed will be skipped at benchmark time." >&2
  }
fi

# 6 — system deps hint
if ! command -v tesseract >/dev/null 2>&1; then
  echo "NOTE: tesseract not on PATH. Install with: brew install tesseract tesseract-lang" >&2
fi
if ! command -v pdftoppm >/dev/null 2>&1; then
  echo "NOTE: poppler not on PATH. Install with: brew install poppler" >&2
fi

# 7 — cache dir env
mkdir -p cache results fixtures/bagrut fixtures/student_photos fixtures/pdfs_mixed fixtures/ground_truth

echo ""
echo "✅ OCR spike environment ready."
echo "   source scripts/ocr-spike/.venv/bin/activate"
echo ""
echo "   export HF_HOME=$(pwd)/cache"
echo "   export TRANSFORMERS_CACHE=$(pwd)/cache"
