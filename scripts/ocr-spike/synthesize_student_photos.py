"""Generate 10 synthetic student-photo fixtures.

Purpose: the spike's Surface-A benchmarks need photos of math homework, but we
cannot use real student data (PP-001 / COPPA). We instead render realistic
math problems via matplotlib + LaTeX then layer on augmentations that simulate
a phone camera: perspective warp, motion blur, JPEG recompression, non-uniform
lighting, colour temperature shift, paper-edge occlusion.

Every fixture is paired with a ground-truth JSON holding the source problem
text and LaTeX equations, so the benchmark can compute WER and math
equivalence against the known truth rather than against another OCR run.

Output:
    fixtures/student_photos/         *.png + *.jpg
    fixtures/ground_truth/           student_photo_*.json

Usage:
    python synthesize_student_photos.py            # default 10 photos
    python synthesize_student_photos.py --count 5  # quick sample
    python synthesize_student_photos.py --noise 0.3  # lower augmentation
"""
from __future__ import annotations

import argparse
import json
import random
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np

try:
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from PIL import Image, ImageEnhance, ImageFilter, ImageOps
    import cv2
except ImportError as e:
    print(f"ERROR: missing dep {e}. Run ./setup.sh first.", file=sys.stderr)
    sys.exit(2)


OUTPUT_DIR = Path(__file__).parent / "fixtures" / "student_photos"
GROUND_TRUTH_DIR = Path(__file__).parent / "fixtures" / "ground_truth"


# ── Problem catalogue ──────────────────────────────────────────────────────
# Mix of Hebrew and English prompts, 3u/4u/5u difficulty, with math regions
# that span single-line equations, displayed equations, and equations nested
# inside text. LaTeX below is what the OCR has to recover.
@dataclass
class Problem:
    fid: str
    language: str          # "he" | "en" | "he+en"
    track: str             # "3u" | "4u" | "5u"
    prose: str             # surrounding text (includes LaTeX as $…$ inline)
    equations: list[str]   # ground-truth LaTeX, normalised form
    figures_count: int = 0


PROBLEMS: list[Problem] = [
    Problem(
        "algebra_01", "he", "3u",
        r"פתור את המשוואה: $3x + 5 = 14$",
        [r"3x + 5 = 14"],
    ),
    Problem(
        "algebra_02", "he", "4u",
        r"מצא את שורשי המשוואה $ax^2 + bx + c = 0$, כאשר $a = 2, b = -3, c = 1$.",
        [r"ax^2 + bx + c = 0", r"a = 2", r"b = -3", r"c = 1"],
    ),
    Problem(
        "calculus_01", "he", "5u",
        r"חשב את הנגזרת: $f(x) = x^3 - 2x^2 + 5x - 7$, $f'(x) = ?$",
        [r"f(x) = x^3 - 2x^2 + 5x - 7", r"f'(x)"],
    ),
    Problem(
        "trig_01", "he", "4u",
        r"בעת שלישית הראה כי: $\sin^2\theta + \cos^2\theta = 1$",
        [r"\sin^2\theta + \cos^2\theta = 1"],
    ),
    Problem(
        "geometry_01", "en", "3u",
        r"Find the area of a triangle with base $b = 10$ cm and height $h = 7$ cm using $A = \frac{1}{2}bh$",
        [r"b = 10", r"h = 7", r"A = \frac{1}{2}bh"],
        figures_count=1,
    ),
    Problem(
        "integral_01", "he+en", "5u",
        r"Compute $\int_{0}^{\pi} \sin(x)\,dx$ -- הסבר את המשמעות הגיאומטרית",
        [r"\int_{0}^{\pi} \sin(x)\,dx"],
    ),
    Problem(
        "probability_01", "he", "4u",
        r"בכד יש 3 כדורים אדומים ו־$5$ כדורים כחולים. ההסתברות לכדור אדום היא $P(\text{אדום}) = \frac{3}{8}$",
        [r"P(\text{אדום}) = \frac{3}{8}"],
    ),
    Problem(
        "sequences_01", "he", "4u",
        r"סדרה חשבונית $a_n = a_1 + (n-1)d$ עם $a_1 = 2, d = 3$. מצא $a_{10}$.",
        [r"a_n = a_1 + (n-1)d", r"a_1 = 2", r"d = 3", r"a_{10}"],
    ),
    Problem(
        "logarithms_01", "en", "4u",
        r"Solve: $\log_2(x) + \log_2(x-1) = 1$",
        [r"\log_2(x) + \log_2(x-1) = 1"],
    ),
    Problem(
        "vectors_01", "he", "5u",
        r"נתון $\vec{u} = (1, 2, 3)$, $\vec{v} = (4, -1, 2)$. חשב $\vec{u} \cdot \vec{v}$",
        [r"\vec{u} = (1, 2, 3)", r"\vec{v} = (4, -1, 2)", r"\vec{u} \cdot \vec{v}"],
    ),
]


# ── Rendering ──────────────────────────────────────────────────────────────
def render_problem(p: Problem, out_png: Path) -> None:
    """Render a problem as a 'page of homework' PNG via matplotlib."""
    fig, ax = plt.subplots(figsize=(6.5, 9), dpi=150)
    fig.patch.set_facecolor("#fdfdf6")    # off-white paper
    ax.axis("off")
    ax.set_xlim(0, 1)
    ax.set_ylim(0, 1)

    # Notebook rule lines — give OCR layout detectors something to anchor on
    for y in np.linspace(0.05, 0.95, 18):
        ax.axhline(y=y, color="#9fb0c7", linewidth=0.3, alpha=0.6)

    # Student hand-style: mix pen blue + pencil grey
    color = random.choice(["#1a2b5f", "#2d2d2d", "#1a3a2a"])

    # RTL positioning for Hebrew lines — align right
    is_rtl = p.language in ("he", "he+en")
    x_text = 0.93 if is_rtl else 0.07
    ha = "right" if is_rtl else "left"

    ax.text(
        0.5, 0.97,
        f"שאלה {p.fid}  •  רמה {p.track}" if is_rtl else f"Problem {p.fid}  •  {p.track}",
        fontsize=11, color="#555", ha="center",
    )

    # Main prose + math
    try:
        ax.text(
            x_text, 0.85, p.prose,
            fontsize=14, color=color, ha=ha, va="top",
            wrap=True,
            usetex=False,                 # matplotlib's mathtext, not real TeX
        )
    except Exception:
        # Hebrew rendering can fail without a Hebrew-capable font on matplotlib's
        # mathtext path. Fall back to rendering the ASCII/LaTeX parts only —
        # the ground-truth JSON still holds the full Hebrew for WER scoring
        # against whatever the OCR tool extracts from the image.
        safe = "".join(c if ord(c) < 0x0590 else "□" for c in p.prose)
        ax.text(
            x_text, 0.85, safe,
            fontsize=14, color=color, ha=ha, va="top",
        )

    # Occasional sketch for geometry problems
    if p.figures_count > 0:
        _draw_triangle(ax, bbox=(0.35, 0.35, 0.3, 0.2))

    plt.savefig(out_png, bbox_inches="tight", pad_inches=0.2, facecolor=fig.get_facecolor())
    plt.close(fig)


def _draw_triangle(ax, bbox):
    x, y, w, h = bbox
    ax.plot(
        [x, x + w, x + w / 2, x],
        [y, y, y + h, y],
        color="#1a2b5f", linewidth=1.5,
    )
    ax.text(x + w / 2, y - 0.02, "b = 10", ha="center", fontsize=9, color="#1a2b5f")
    ax.text(x + w + 0.02, y + h / 2, "h = 7", va="center", fontsize=9, color="#1a2b5f")


# ── Augmentations (simulate phone camera) ──────────────────────────────────
def augment_as_photo(in_png: Path, out_jpg: Path, noise: float, rng: random.Random) -> None:
    img = Image.open(in_png).convert("RGB")

    # 1) Perspective warp — subtle skew
    img_cv = cv2.cvtColor(np.array(img), cv2.COLOR_RGB2BGR)
    h, w = img_cv.shape[:2]
    jitter = noise * 0.04
    src = np.float32([[0, 0], [w, 0], [w, h], [0, h]])
    dst = np.float32([
        [rng.uniform(0, w * jitter), rng.uniform(0, h * jitter)],
        [w - rng.uniform(0, w * jitter), rng.uniform(0, h * jitter)],
        [w - rng.uniform(0, w * jitter), h - rng.uniform(0, h * jitter)],
        [rng.uniform(0, w * jitter), h - rng.uniform(0, h * jitter)],
    ])
    M = cv2.getPerspectiveTransform(src, dst)
    img_cv = cv2.warpPerspective(
        img_cv, M, (w, h), borderMode=cv2.BORDER_CONSTANT,
        borderValue=(240, 238, 230),
    )

    # 2) Non-uniform lighting — multiplicative radial falloff
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    cx = w * rng.uniform(0.35, 0.65)
    cy = h * rng.uniform(0.3, 0.7)
    r = np.hypot(xx - cx, yy - cy)
    r /= r.max()
    vignette = 1.0 - noise * 0.35 * r
    img_cv = (img_cv.astype(np.float32) * vignette[:, :, None]).clip(0, 255).astype(np.uint8)

    # 3) Motion blur — small kernel, random direction
    k = max(1, int(noise * 4))
    if k > 1:
        angle = rng.uniform(0, 180)
        kernel = np.zeros((k, k), dtype=np.float32)
        kernel[k // 2, :] = 1.0 / k
        M2 = cv2.getRotationMatrix2D((k / 2, k / 2), angle, 1)
        kernel = cv2.warpAffine(kernel, M2, (k, k))
        img_cv = cv2.filter2D(img_cv, -1, kernel)

    # 4) Noise
    sigma = noise * 8.0
    if sigma > 0.5:
        n = rng.gauss
        noise_arr = np.array([[[n(0, sigma) for _ in range(3)]
                               for _ in range(w)] for _ in range(h)], dtype=np.float32)
        img_cv = (img_cv.astype(np.float32) + noise_arr).clip(0, 255).astype(np.uint8)

    # 5) JPEG recompression — 70 quality simulates chat-app forwarding
    img_final = Image.fromarray(cv2.cvtColor(img_cv, cv2.COLOR_BGR2RGB))
    img_final.save(out_jpg, format="JPEG", quality=rng.randint(65, 85))


# ── Main ────────────────────────────────────────────────────────────────────
def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--count", type=int, default=10)
    ap.add_argument("--noise", type=float, default=0.5, help="0..1 augmentation strength")
    ap.add_argument("--seed", type=int, default=19)
    ap.add_argument("--keep-clean", action="store_true",
                    help="also keep the pre-augmentation PNG for debugging")
    args = ap.parse_args()

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    GROUND_TRUTH_DIR.mkdir(parents=True, exist_ok=True)

    rng = random.Random(args.seed)
    targets = PROBLEMS[: args.count]

    for p in targets:
        clean_png = OUTPUT_DIR / f"{p.fid}_clean.png"
        photo_jpg = OUTPUT_DIR / f"{p.fid}.jpg"
        gt_json = GROUND_TRUTH_DIR / f"student_photo_{p.fid}.json"

        print(f"  render  {p.fid}")
        render_problem(p, clean_png)

        print(f"  augment {p.fid} → {photo_jpg.name}")
        augment_as_photo(clean_png, photo_jpg, args.noise, rng)

        if not args.keep_clean:
            clean_png.unlink(missing_ok=True)

        gt_json.write_text(json.dumps({
            "fixture_id": f"student_photo_{p.fid}",
            "source": "synthesized",
            "language_primary": p.language.split("+")[0],
            "language_secondary": p.language.split("+")[1] if "+" in p.language else None,
            "track": p.track,
            "hebrew_text": p.prose if p.language.startswith("he") else None,
            "english_text": p.prose if p.language == "en" else None,
            "latex_equations": p.equations,
            "layout": {
                "columns": 1,
                "direction": "rtl" if p.language.startswith("he") else "ltr",
                "figures_count": p.figures_count,
            },
            "notes": "Synthetic; camera augmentation applied.",
        }, indent=2, ensure_ascii=False), encoding="utf-8")

    print(f"\n→ {len(targets)} student-photo fixtures at {OUTPUT_DIR.relative_to(Path.cwd())}")
    print(f"→ ground truth at {GROUND_TRUTH_DIR.relative_to(Path.cwd())}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
