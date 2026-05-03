"""Build 5 mixed-content PDFs for PDF-triage benchmarking.

The four triage categories the cascade must discriminate:
  1. text-PDF           — pypdf extracts usable text layer
  2. image-only PDF     — pages are rasterised images, no text layer
  3. mixed text + image — text layer present but figures are images
  4. scanned-with-OCR'd layer — text layer exists but is often wrong/garbage
  5. encrypted          — password-protected or restricted

Built from scratch using reportlab + PIL so we have predictable ground truth
for the triage classifier. Output goes to `fixtures/pdfs_mixed/` and ground
truth to `fixtures/ground_truth/pdf_mixed_*.json`.
"""
from __future__ import annotations

import argparse
import io
import json
import sys
from pathlib import Path

try:
    from reportlab.lib.pagesizes import A4
    from reportlab.pdfgen import canvas
    from reportlab.lib.utils import ImageReader
    from PIL import Image, ImageDraw, ImageFont
    import pypdf
except ImportError as e:
    print(f"ERROR: missing dep {e}. Try `pip install reportlab` in the venv.", file=sys.stderr)
    sys.exit(2)


OUTPUT_DIR = Path(__file__).parent / "fixtures" / "pdfs_mixed"
GT_DIR = Path(__file__).parent / "fixtures" / "ground_truth"


def _rasterize_text_as_image(text: str, size=(600, 800)) -> Image.Image:
    img = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(img)
    try:
        font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 24)
    except Exception:
        font = ImageFont.load_default()
    draw.multiline_text((40, 40), text, fill="black", font=font, spacing=8)
    return img


# ── Builders ───────────────────────────────────────────────────────────────
def build_text_pdf(out: Path) -> dict:
    c = canvas.Canvas(str(out), pagesize=A4)
    w, h = A4
    c.setFont("Helvetica", 14)
    c.drawString(50, h - 80, "Problem 1: Solve 3x + 5 = 14")
    c.drawString(50, h - 110, "Problem 2: Find the derivative of f(x) = x^2.")
    c.drawString(50, h - 140, "Problem 3: Compute the integral of sin(x) from 0 to pi.")
    c.save()
    return {
        "type": "text_pdf",
        "expected_text_layer": True,
        "expected_images": False,
        "pages": 1,
    }


def build_image_only_pdf(out: Path) -> dict:
    c = canvas.Canvas(str(out), pagesize=A4)
    w, h = A4
    img = _rasterize_text_as_image(
        "Bagrut 5u Summer 2022\n\n"
        "שאלה 1: פתור את המשוואה\n"
        "    3x + 5 = 14\n\n"
        "שאלה 2: חשב את הנגזרת של\n"
        "    f(x) = x^3 - 2x + 1\n"
    )
    bio = io.BytesIO()
    img.save(bio, format="PNG")
    bio.seek(0)
    c.drawImage(ImageReader(bio), 50, 100, width=w - 100, height=h - 200)
    c.save()
    return {
        "type": "image_only_pdf",
        "expected_text_layer": False,
        "expected_images": True,
        "pages": 1,
    }


def build_mixed_pdf(out: Path) -> dict:
    c = canvas.Canvas(str(out), pagesize=A4)
    w, h = A4
    c.setFont("Helvetica", 14)
    c.drawString(50, h - 80, "Problem 1: Figure below shows a triangle.")

    tri_img = Image.new("RGB", (400, 200), "white")
    draw = ImageDraw.Draw(tri_img)
    draw.polygon([(100, 180), (300, 180), (200, 40)], outline="black", width=3)
    draw.text((180, 185), "b = 10", fill="black")
    draw.text((310, 90), "h = 7", fill="black")
    bio = io.BytesIO()
    tri_img.save(bio, format="PNG")
    bio.seek(0)
    c.drawImage(ImageReader(bio), 80, h - 340, width=400, height=200)

    c.setFont("Helvetica", 14)
    c.drawString(50, h - 380, "Find the area using A = (1/2) * b * h.")
    c.save()
    return {
        "type": "mixed_pdf",
        "expected_text_layer": True,
        "expected_images": True,
        "pages": 1,
    }


def build_scanned_with_ocr_layer_pdf(out: Path) -> dict:
    """Create an image-only PDF then overlay intentionally-wrong invisible text.

    This simulates the worst real-world case: a third-party OCR produced a
    text layer but it's garbage, and downstream pipelines that trust the
    text layer will emit nonsense.
    """
    c = canvas.Canvas(str(out), pagesize=A4)
    w, h = A4
    img = _rasterize_text_as_image("Problem 1: Solve 3x + 5 = 14")
    bio = io.BytesIO()
    img.save(bio, format="PNG")
    bio.seek(0)
    c.drawImage(ImageReader(bio), 50, 100, width=w - 100, height=h - 200)

    # Deliberately wrong text layer — simulating a bad third-party OCR result:
    # real-world "scanned_with_OCR'd_layer" garbage is typically codepoint
    # noise from misidentified glyphs, not coherent prose. We synthesize that
    # with high-frequency diacritics + control-range chars + reversed CJK.
    c.setFillColorRGB(1, 1, 1)           # invisible — white text on white
    c.setFont("Helvetica", 1)
    garbage = (
        "\uFB01\uFB02\u00BF\u00A1\u00AF\u00A7\u2020\u2021\u00B5\u00A9 "
        "\u0483\u0484\u0485 \uFFFD\uFFFD\uFFFD "
        "xvqzjk qzhxw \u0394\u039E\u03A9\u2202\u222B"
    )
    c.drawString(50, h - 150, garbage * 3)
    c.save()
    return {
        "type": "scanned_with_ocr_layer",
        "expected_text_layer": True,
        "expected_text_layer_reliable": False,
        "expected_images": True,
        "pages": 1,
    }


def build_encrypted_pdf(out: Path) -> dict:
    from reportlab.lib.pdfencrypt import StandardEncryption
    encrypt = StandardEncryption(
        userPassword="readerpass", ownerPassword="ownerpass",
        canPrint=1, canModify=0, canCopy=0, canAnnotate=0,
        strength=128,
    )
    c = canvas.Canvas(str(out), pagesize=A4, encrypt=encrypt)
    c.setFont("Helvetica", 14)
    c.drawString(50, 700, "Encrypted fixture — should require owner password to open.")
    c.save()
    return {
        "type": "encrypted_pdf",
        "expected_text_layer": True,
        "expected_images": False,
        "encrypted": True,
        "pages": 1,
        "password_hint": "readerpass",
    }


BUILDERS = {
    "pdf_01_text_only.pdf": build_text_pdf,
    "pdf_02_image_only.pdf": build_image_only_pdf,
    "pdf_03_mixed.pdf": build_mixed_pdf,
    "pdf_04_scanned_bad_ocr_layer.pdf": build_scanned_with_ocr_layer_pdf,
    "pdf_05_encrypted.pdf": build_encrypted_pdf,
}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--clean", action="store_true")
    args = ap.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    GT_DIR.mkdir(parents=True, exist_ok=True)

    if args.clean:
        for f in OUTPUT_DIR.glob("*.pdf"):
            f.unlink()

    for name, fn in BUILDERS.items():
        out = OUTPUT_DIR / name
        print(f"  build  {name}")
        gt = fn(out)
        GT_DIR.joinpath(f"pdf_mixed_{name.replace('.pdf', '')}.json").write_text(
            json.dumps({
                "fixture_id": f"pdf_mixed_{name.replace('.pdf', '')}",
                "source": "synthesized",
                **gt,
            }, indent=2, ensure_ascii=False),
            encoding="utf-8",
        )

    # sanity — does pypdf see what we expect?
    print("\nVerification via pypdf:")
    for name in BUILDERS:
        p = OUTPUT_DIR / name
        try:
            reader = pypdf.PdfReader(str(p))
            encrypted = reader.is_encrypted
            if encrypted:
                print(f"  {name}  encrypted=True (cannot read without password)")
                continue
            text = "".join(pg.extract_text() or "" for pg in reader.pages)
            print(f"  {name}  text_layer_chars={len(text.strip())}")
        except Exception as e:
            print(f"  {name}  error={e}")

    print(f"\n→ {len(BUILDERS)} mixed-content PDFs at {OUTPUT_DIR.relative_to(Path.cwd())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
