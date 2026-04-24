#!/usr/bin/env python3
"""
Cena Platform — Bagrut Reference Scraper (RDY-019b / Phase 3)

Downloads Israeli Ministry of Education Bagrut mathematics exam PDFs from
the public archive at meyda.education.gov.il/sheeloney_bagrut/ for
**reference-only** structural analysis. Raw PDFs stay local (under
corpus/bagrut/reference/, git-ignored) — student-facing items are
AI-authored CAS-gated recreations per ADR-0033 + memory:bagrut_reference_only.

Polite-crawl defaults:
  * 2-second delay between requests
  * respects robots.txt (checked at startup)
  * resumes from checkpoint file (corpus/bagrut/reference/.checkpoint.json)

Usage:
    python scripts/bagrut-scraper.py --track 5u --year 2020 --year 2021
    python scripts/bagrut-scraper.py --dry-run        # list URLs, download nothing
    python scripts/bagrut-scraper.py --resume          # pick up from checkpoint

Requires: requests, beautifulsoup4  (install via: pip install -r requirements.txt)

IMPORTANT — legal posture
=========================
This script does NOT redistribute Ministry content. PDFs are downloaded to
a local git-ignored directory and used only as input to the structural
analyzer at scripts/bagrut-reference-analyzer.py, which emits aggregate
topic × difficulty × format distributions. Individual question text is
NEVER persisted to Marten or published to students — see ADR-0033.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import logging
import sys
import time
import urllib.parse
import urllib.robotparser
from dataclasses import dataclass
from pathlib import Path

try:
    import requests
    from bs4 import BeautifulSoup
except ImportError:
    sys.stderr.write(
        "bagrut-scraper.py: requests + beautifulsoup4 required. "
        "Install: pip install requests beautifulsoup4\n")
    sys.exit(2)

BASE_URL = "https://meyda.education.gov.il/sheeloney_bagrut/"
USER_AGENT = "Cena-Bagrut-Reference-Crawler/1.0 (+https://github.com/shakeraa/cena; bagrut-reference-only)"
POLITE_DELAY_SECONDS = 2.0
TIMEOUT_SECONDS = 30

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
log = logging.getLogger("bagrut-scraper")


@dataclass
class ExamPaper:
    year: int
    track: str          # "3u" | "4u" | "5u"
    exam_code: str
    url: str
    local_path: Path


def check_robots(session: requests.Session) -> bool:
    rp = urllib.robotparser.RobotFileParser()
    rp.set_url(urllib.parse.urljoin(BASE_URL, "/robots.txt"))
    try:
        rp.read()
    except Exception as ex:
        log.warning("robots.txt fetch failed: %s — aborting out of caution", ex)
        return False
    ok = rp.can_fetch(USER_AGENT, BASE_URL)
    log.info("robots.txt allows crawl=%s", ok)
    return ok


def discover_papers(session: requests.Session, tracks: list[str], years: list[int]) -> list[ExamPaper]:
    """Placeholder: the Ministry site's directory structure changes per
    academic year. The real implementation walks the index pages and
    filters by track + year. Kept as a stub loop to document the shape —
    run in DRY-RUN mode first to confirm URL patterns before committing."""
    log.warning(
        "discover_papers: NOT YET CONFIGURED for the current Ministry site "
        "layout. Populate via manual URL list or extend with the site-"
        "specific directory walker before production crawls.")
    return []


def download_pdf(session: requests.Session, paper: ExamPaper) -> bool:
    paper.local_path.parent.mkdir(parents=True, exist_ok=True)
    if paper.local_path.exists():
        log.info("skip existing %s", paper.local_path)
        return True
    log.info("download %s → %s", paper.url, paper.local_path)
    r = session.get(paper.url, timeout=TIMEOUT_SECONDS)
    if r.status_code != 200:
        log.error("HTTP %s on %s", r.status_code, paper.url)
        return False
    paper.local_path.write_bytes(r.content)
    time.sleep(POLITE_DELAY_SECONDS)
    return True


def load_checkpoint(path: Path) -> dict:
    return json.loads(path.read_text()) if path.exists() else {"downloaded": []}


def save_checkpoint(path: Path, state: dict) -> None:
    path.write_text(json.dumps(state, indent=2))


def main() -> int:
    ap = argparse.ArgumentParser(description="Cena Bagrut reference scraper")
    ap.add_argument("--track", action="append", default=[], help="Tracks: 3u | 4u | 5u (repeatable)")
    ap.add_argument("--year", action="append", type=int, default=[], help="Years (repeatable)")
    ap.add_argument("--dry-run", action="store_true", help="Discover URLs only, no downloads")
    ap.add_argument("--resume", action="store_true", help="Resume from checkpoint")
    ap.add_argument("--out", type=Path, default=Path("corpus/bagrut/reference"))
    args = ap.parse_args()

    tracks = args.track or ["3u", "4u", "5u"]
    years = args.year or list(range(2018, 2025))

    session = requests.Session()
    session.headers["User-Agent"] = USER_AGENT

    if not check_robots(session):
        log.error("robots.txt disallows crawl — aborting.")
        return 1

    checkpoint_path = args.out / ".checkpoint.json"
    state = load_checkpoint(checkpoint_path) if args.resume else {"downloaded": []}

    papers = discover_papers(session, tracks, years)
    if not papers:
        log.warning("No papers discovered — scraper needs site-layout configuration before production runs.")
        return 2

    ok = 0
    for paper in papers:
        key = hashlib.sha256(paper.url.encode()).hexdigest()
        if key in state["downloaded"]:
            continue
        if args.dry_run:
            log.info("DRY-RUN %s", paper.url)
            ok += 1
            continue
        if download_pdf(session, paper):
            state["downloaded"].append(key)
            save_checkpoint(checkpoint_path, state)
            ok += 1

    log.info("done: %d/%d papers processed", ok, len(papers))
    return 0


if __name__ == "__main__":
    sys.exit(main())
