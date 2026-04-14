#!/usr/bin/env python3
"""
RDY-019: Bagrut Exam Corpus Scraper Framework

Downloads Bagrut exam PDFs from the Ministry of Education archive,
extracts questions using OCR, and structures them as QuestionDocument
events for ingestion into the Cena question bank.

Source: https://meyda.education.gov.il/sheeloney_bagrut/

Usage:
    python scripts/bagrut-scraper.py --exam-code 806 --year 2024
    python scripts/bagrut-scraper.py --list-available
    python scripts/bagrut-scraper.py --extract --input exams/806_2024.pdf --output questions/

Prerequisites:
    pip install requests pdfplumber

NOTE: Actual OCR integration requires Gemini Vision or Mathpix API keys.
      This script provides the framework; math OCR uses the existing
      GeminiOcrClient / MathpixClient from the Cena pipeline.
"""

import argparse
import json
import sys
from pathlib import Path
from datetime import datetime

# Bagrut exam codes for math tracks
EXAM_CODES = {
    "803": {"name": "Mathematics 3-Unit", "track": "math_3u"},
    "804": {"name": "Mathematics 4-Unit", "track": "math_4u"},
    "806": {"name": "Mathematics 5-Unit (Calc+Geometry)", "track": "math_5u"},
    "807": {"name": "Mathematics 5-Unit (Calc+Probability)", "track": "math_5u"},
    "036": {"name": "Physics", "track": "physics"},
}

# Ministry archive base URL
ARCHIVE_BASE = "https://meyda.education.gov.il/sheeloney_bagrut"

# Load taxonomy for topic mapping
TAXONOMY_PATH = Path(__file__).parent / "bagrut-taxonomy.json"


def load_taxonomy():
    """Load the Bagrut topic taxonomy."""
    if not TAXONOMY_PATH.exists():
        print(f"ERROR: Taxonomy file not found at {TAXONOMY_PATH}")
        sys.exit(1)
    with open(TAXONOMY_PATH) as f:
        return json.load(f)


def list_available():
    """List available exam codes and their tracks."""
    print("Available Bagrut exam codes:")
    print(f"{'Code':<8} {'Name':<45} {'Track'}")
    print("-" * 70)
    for code, info in EXAM_CODES.items():
        print(f"{code:<8} {info['name']:<45} {info['track']}")


def generate_download_url(exam_code: str, year: int, moed: str = "A") -> str:
    """Generate the expected URL for a Bagrut exam PDF."""
    # Ministry URL pattern (may vary by year)
    return f"{ARCHIVE_BASE}/{exam_code}_{year}_{moed}.pdf"


def extract_questions_from_pdf(pdf_path: str, exam_code: str) -> list[dict]:
    """
    Extract questions from a Bagrut exam PDF.

    This is the framework — actual implementation requires:
    1. pdfplumber for text extraction
    2. Gemini Vision / Mathpix for math OCR
    3. Heuristic page/question boundary detection

    Returns a list of structured question dicts.
    """
    questions = []

    # Framework: each extracted question should have this structure
    template = {
        "stem": "",           # Question text (Hebrew)
        "stemHtml": "",       # HTML-formatted stem
        "subject": "Math",
        "topic": "",          # Mapped from taxonomy
        "grade": "",          # "3 Units" / "4 Units" / "5 Units"
        "bloomLevel": 3,      # Estimated from question type
        "difficulty": 0.5,    # Calibrated later via IRT
        "concepts": [],       # Concept IDs from taxonomy
        "language": "he",
        "source": "ingested",
        "sourceDocument": pdf_path,
        "sourceUrl": "",
        "examCode": exam_code,
        "examYear": 0,
        "bagrutAlignment": {
            "examCode": exam_code,
            "part": "",       # "A" or "B"
            "typicalPosition": None,
            "topicCluster": "",
            "isProofQuestion": False,
            "estimatedMinutes": 15,
        },
        "options": [],        # For MCQ: [{label, text, isCorrect, rationale}]
        "explanation": "",    # Solution explanation
    }

    print(f"  Framework: Would extract questions from {pdf_path}")
    print(f"  Exam code: {exam_code}")
    print(f"  NOTE: Actual OCR requires Gemini Vision or Mathpix API keys")
    print(f"  Template structure: {list(template.keys())}")

    return questions


def map_to_taxonomy(question: dict, taxonomy: dict) -> dict:
    """Map an extracted question to the formal taxonomy."""
    track = EXAM_CODES.get(question.get("examCode", ""), {}).get("track", "math_5u")
    track_data = taxonomy.get("tracks", {}).get(track, {})

    # TODO: Use NLP / keyword matching to map question stem to taxonomy topic
    # For now, return the question with empty concept mapping
    return question


def coverage_report(taxonomy: dict, questions: list[dict]) -> dict:
    """Generate a coverage report: questions per taxonomy node."""
    report = {}

    for track_id, track_data in taxonomy.get("tracks", {}).items():
        track_report = {"name": track_data["name"], "topics": {}}

        for topic_id, topic_data in track_data.get("topics", {}).items():
            topic_report = {"name": topic_data["name"], "subtopics": {}}

            for subtopic_id, subtopic_data in topic_data.get("subtopics", {}).items():
                concept_id = subtopic_data.get("conceptId", "")
                count = sum(1 for q in questions if concept_id in q.get("concepts", []))
                topic_report["subtopics"][subtopic_id] = {
                    "conceptId": concept_id,
                    "questionCount": count,
                    "gap": count == 0,
                }

            topic_report["totalQuestions"] = sum(
                s["questionCount"] for s in topic_report["subtopics"].values()
            )
            topic_report["gaps"] = [
                s_id for s_id, s in topic_report["subtopics"].items() if s["gap"]
            ]
            track_report["topics"][topic_id] = topic_report

        report[track_id] = track_report

    return report


def main():
    parser = argparse.ArgumentParser(description="Bagrut Exam Corpus Scraper")
    parser.add_argument("--list-available", action="store_true", help="List exam codes")
    parser.add_argument("--exam-code", type=str, help="Exam code (e.g., 806)")
    parser.add_argument("--year", type=int, help="Exam year")
    parser.add_argument("--extract", action="store_true", help="Extract from local PDF")
    parser.add_argument("--input", type=str, help="Input PDF path")
    parser.add_argument("--output", type=str, default="questions/", help="Output directory")
    parser.add_argument("--coverage", action="store_true", help="Show coverage report")
    args = parser.parse_args()

    if args.list_available:
        list_available()
        return

    taxonomy = load_taxonomy()

    if args.coverage:
        # Generate coverage report with current seed data (empty for now)
        report = coverage_report(taxonomy, [])
        print(json.dumps(report, indent=2, ensure_ascii=False))
        return

    if args.extract and args.input:
        exam_code = args.exam_code or "806"
        questions = extract_questions_from_pdf(args.input, exam_code)

        if questions:
            output_dir = Path(args.output)
            output_dir.mkdir(parents=True, exist_ok=True)
            output_file = output_dir / f"bagrut_{exam_code}_{datetime.now():%Y%m%d}.json"
            with open(output_file, "w", encoding="utf-8") as f:
                json.dump(questions, f, indent=2, ensure_ascii=False)
            print(f"Extracted {len(questions)} questions to {output_file}")
        return

    if args.exam_code and args.year:
        url = generate_download_url(args.exam_code, args.year)
        print(f"Download URL: {url}")
        print("NOTE: Automatic download not implemented. Download manually and use --extract.")
        return

    parser.print_help()


if __name__ == "__main__":
    main()
