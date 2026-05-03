"""Acquire a small Bagrut-PDF fixture set for OCR benchmarking.

Two modes:

    --local <dir>   Copy a curated slice from a local directory of Bagrut PDFs.
                    Preferred for this spike because the user already has
                    ~86 Math Bagrut exams on disk.

    --network       Fetch from the public Ministry archive
                    `meyda.education.gov.il/sheeloney_bagrut/`. Used if local
                    isn't available. Polite pacing, fail-soft.

Target: 10 single-page (or short multi-page) PDFs. The spike only needs enough
pages to build a representative distribution of per-region confidence. The
full 640-page scrape lives in RDY-019b and must not run here.

Per the `bagrut-reference-only` memory (2026-04-15) the files are reference
only — git-ignored, never shipped to students.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import random
import shutil
import sys
import time
from pathlib import Path

try:
    import httpx
except ImportError:
    httpx = None  # type: ignore[assignment]


TARGET_COUNT = 10
OUTPUT_DIR = Path(__file__).parent / "fixtures" / "bagrut"
MANIFEST = OUTPUT_DIR / "manifest.json"
USER_AGENT = (
    "Mozilla/5.0 (Macintosh; Apple Silicon) Cena-OCR-Spike/1.0 "
    "(educational research; 10-file fixture set)"
)
REQUEST_TIMEOUT = 30.0
SLEEP_BETWEEN = 0.8


# ── Network mode (fallback) ─────────────────────────────────────────────────
CANDIDATE_URLS: list[tuple[str, str]] = [
    ("bagrut_math_3u_2023_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2023/4/HEB/035381.pdf"),
    ("bagrut_math_4u_2023_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2023/4/HEB/035481.pdf"),
    ("bagrut_math_5u_2023_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2023/4/HEB/035581.pdf"),
    ("bagrut_math_3u_2022_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2022/4/HEB/035381.pdf"),
    ("bagrut_math_4u_2022_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2022/4/HEB/035481.pdf"),
    ("bagrut_math_5u_2022_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2022/4/HEB/035581.pdf"),
    ("bagrut_math_3u_2021_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2021/4/HEB/035381.pdf"),
    ("bagrut_math_4u_2021_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2021/4/HEB/035481.pdf"),
    ("bagrut_math_5u_2021_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2021/4/HEB/035581.pdf"),
    ("bagrut_math_5u_2020_summer.pdf",
     "https://meyda.education.gov.il/sheeloney_bagrut/2020/4/HEB/035581.pdf"),
]


# ── Local mode (preferred — user already has the corpus) ────────────────────
def pick_local_sample(root: Path, target: int, rng: random.Random) -> list[Path]:
    """Pick a diverse sample: roughly even spread across 3u / 4u / 5u, Hebrew exams only.

    Avoids:
      - solutions (file name contains `solution`)
      - Arabic variants (file name contains `arabic`)
      - `other` bucket (unknown unit)
      - `unknown_units`

    Preferred: `exam` files, Hebrew only, across 3_units / 4_units / 5_units.
    """
    groups: dict[str, list[Path]] = {"3_units": [], "4_units": [], "5_units": []}
    for unit in ("3_units", "4_units", "5_units"):
        unit_dir = root / "מתמטיקה" / unit
        if not unit_dir.exists():
            continue
        for p in unit_dir.iterdir():
            if p.suffix.lower() != ".pdf":
                continue
            name = p.name.lower()
            if "solution" in name or "arabic" in name:
                continue
            if "exam" not in name:
                # Geva-style files (שאלון_…) — keep them too; they're exam-style
                pass
            groups[unit].append(p)

    # Distribute: ceil(target/3) per bucket, shuffled
    selected: list[Path] = []
    per = max(1, target // 3)
    extras = target - per * 3
    for i, unit in enumerate(("3_units", "4_units", "5_units")):
        files = groups[unit][:]
        rng.shuffle(files)
        take = per + (1 if i < extras else 0)
        selected.extend(files[:take])

    # Top up from any bucket if we fell short
    pool = [p for unit in groups.values() for p in unit]
    rng.shuffle(pool)
    seen = {p.resolve() for p in selected}
    for p in pool:
        if len(selected) >= target:
            break
        if p.resolve() not in seen:
            selected.append(p)
            seen.add(p.resolve())
    return selected[:target]


def copy_to_fixtures(src: Path, dst_dir: Path) -> Path:
    """Copy and sanitize the filename (ASCII-safe for downstream tools)."""
    dst_dir.mkdir(parents=True, exist_ok=True)
    # Keep a stable ASCII filename: strip non-ASCII, preserve original in manifest
    safe = "".join(c if (32 <= ord(c) < 127) else "_" for c in src.name)
    safe = safe.replace(" ", "_").replace("__", "_")
    dst = dst_dir / safe
    if dst.exists():
        dst.unlink()
    shutil.copy2(src, dst)
    return dst


# ── Network mode ────────────────────────────────────────────────────────────
def fetch_one(url: str, out_path: Path, client: "httpx.Client") -> tuple[bool, str]:
    try:
        resp = client.get(url, timeout=REQUEST_TIMEOUT, follow_redirects=True)
    except Exception as e:
        return False, f"network:{type(e).__name__}:{e}"
    if resp.status_code == 404:
        return False, "404"
    if resp.status_code != 200:
        return False, f"http_{resp.status_code}"
    content = resp.content
    if not content.startswith(b"%PDF"):
        return False, "not_a_pdf"
    out_path.write_bytes(content)
    return True, f"ok_{len(content)}_bytes"


# ── Common ──────────────────────────────────────────────────────────────────
def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--local", type=Path, default=None,
                   help="copy from local dir (e.g. ~/Documents/cena-files/downloaded_pdfs/bagrut)")
    p.add_argument("--network", action="store_true",
                   help="force network mode (Ministry archive)")
    p.add_argument("--clean", action="store_true", help="wipe fixtures/bagrut/ first")
    p.add_argument("--target", type=int, default=TARGET_COUNT)
    p.add_argument("--seed", type=int, default=19)
    args = p.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    if args.clean:
        for f in OUTPUT_DIR.glob("*.pdf"):
            f.unlink()
        if MANIFEST.exists():
            MANIFEST.unlink()

    # ── Prefer local if --local given ───────────────────────────────────────
    if args.local and not args.network:
        if not args.local.exists():
            print(f"ERROR: local dir {args.local} not found", file=sys.stderr)
            return 2
        rng = random.Random(args.seed)
        picks = pick_local_sample(args.local, args.target, rng)
        if not picks:
            print(f"ERROR: no suitable PDFs under {args.local}/מתמטיקה/{{3,4,5}}_units/",
                  file=sys.stderr)
            return 2
        manifest: list[dict] = []
        for src in picks:
            dst = copy_to_fixtures(src, OUTPUT_DIR)
            unit = None
            for u in ("3_units", "4_units", "5_units"):
                if f"/{u}/" in str(src):
                    unit = u
                    break
            manifest.append({
                "filename": dst.name,
                "source": "local",
                "original_path": str(src),
                "unit": unit,
                "sha256": sha256(dst),
                "bytes": dst.stat().st_size,
            })
            print(f"  copy  {src.name} → {dst.name}")
        MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False))
        print(f"\n→ {len(manifest)} Bagrut PDFs acquired via --local")
        print(f"→ manifest at {MANIFEST.relative_to(Path.cwd()) if Path.cwd() in MANIFEST.parents else MANIFEST}")
        return 0

    # ── Network mode ────────────────────────────────────────────────────────
    if httpx is None:
        print("ERROR: httpx not installed and --local not given. Run ./setup.sh first.",
              file=sys.stderr)
        return 2

    manifest = []
    if MANIFEST.exists():
        manifest = json.loads(MANIFEST.read_text())
    already = {entry["filename"] for entry in manifest}
    successes = sum(1 for e in manifest if e.get("ok"))

    headers = {"User-Agent": USER_AGENT, "Accept": "application/pdf,*/*"}
    with httpx.Client(headers=headers, http2=False) as client:
        for name, url in CANDIDATE_URLS:
            if successes >= args.target:
                break
            out_path = OUTPUT_DIR / name
            if out_path.exists() and name in already:
                continue
            print(f"  GET   {url}")
            ok, reason = fetch_one(url, out_path, client)
            entry = {
                "filename": name, "url": url, "ok": ok, "reason": reason,
                "sha256": sha256(out_path) if ok else None,
                "bytes": out_path.stat().st_size if ok else 0,
            }
            manifest.append(entry)
            if ok:
                successes += 1
                print(f"  OK    {name} ({entry['bytes']} bytes)")
            else:
                print(f"  FAIL  {name} — {reason}")
            time.sleep(SLEEP_BETWEEN)

    MANIFEST.write_text(json.dumps(manifest, indent=2, ensure_ascii=False))
    print(f"\n→ {successes}/{args.target} Bagrut PDFs acquired")
    return 0 if successes >= args.target else 1


if __name__ == "__main__":
    sys.exit(main())
