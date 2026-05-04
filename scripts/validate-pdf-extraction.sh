#!/usr/bin/env bash
# =============================================================================
# scripts/validate-pdf-extraction.sh
#
# Compares PDF extraction across paths so you can see, side-by-side per page:
#   1. Page rendered as PNG @ 150 DPI (the "what the model sees" view).
#   2. pdftohtml structural output — text in original positions, with bbox
#      markers; the closest baseline to "preserve visual structure".
#   3. pdftotext -layout raw text — the simplest direct-text path.
#
# Output: <out-dir>/index.html — a single HTML page that loads the PDF page
# PNG on the left and the two extraction columns on the right, per page.
#
# Usage:
#   scripts/validate-pdf-extraction.sh <input.pdf> <out-dir>
#
# Example:
#   scripts/validate-pdf-extraction.sh corpus/tests/35581-q.pdf \
#     /tmp/cena-validate/35581
#
# Then open <out-dir>/index.html in a browser.
#
# Dependencies: poppler-utils (pdftotext, pdftoppm, pdftohtml). On macOS:
#   brew install poppler
# Already installed inside the cena-admin-api container's `dev` Dockerfile.
# =============================================================================
set -euo pipefail

PDF="${1:?usage: $0 <input.pdf> <out-dir>}"
OUT="${2:?usage: $0 <input.pdf> <out-dir>}"

if [[ ! -f "$PDF" ]]; then
  echo "Input PDF not found: $PDF" >&2
  exit 1
fi

# Need a clean directory so stale outputs from a prior run don't leak in.
rm -rf "$OUT"
mkdir -p "$OUT/png" "$OUT/html" "$OUT/text"

echo "[1/4] Rendering pages to PNG @ 150 DPI..."
pdftoppm -r 150 -png "$PDF" "$OUT/png/page" >/dev/null 2>&1

echo "[2/4] Extracting layout-preserved text via pdftotext..."
pdftotext -layout "$PDF" "$OUT/text/all.txt"
# Per-page split — pdftotext -f / -l flags accept page ranges.
PAGES=$(pdfinfo "$PDF" | awk '/^Pages:/ {print $2}')
for ((p=1; p<=PAGES; p++)); do
  pdftotext -layout -f "$p" -l "$p" "$PDF" "$OUT/text/page-$p.txt"
done

echo "[3/4] Extracting structured HTML (text in original positions)..."
# -c = complex output (positioned divs); per-page files are emitted.
# -nodrm suppresses the password prompt for tagged PDFs.
pdftohtml -c -nodrm "$PDF" "$OUT/html/page" >/dev/null 2>&1 || true

echo "[4/4] Assembling side-by-side validation HTML..."

PDF_BASENAME=$(basename "$PDF" .pdf)
INDEX="$OUT/index.html"

cat > "$INDEX" <<EOF
<!doctype html>
<html lang="en" dir="ltr">
<head>
<meta charset="utf-8">
<title>Cena PDF extraction validation — $PDF_BASENAME</title>
<style>
  body { font-family: system-ui, -apple-system, sans-serif; margin: 0; padding: 1rem 2rem; background: #1a1a1a; color: #e0e0e0; }
  h1 { font-size: 1.5rem; margin: 0 0 1rem; }
  .meta { color: #999; font-size: 0.9rem; margin-bottom: 2rem; }
  .page { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 1.5rem; margin-bottom: 3rem; padding: 1.5rem; background: #222; border-radius: 0.5rem; }
  .page-header { grid-column: 1 / -1; font-size: 1.1rem; font-weight: 600; color: #aaa; }
  .col-snapshot img { width: 100%; height: auto; border: 1px solid #444; background: white; border-radius: 0.25rem; }
  .col-snapshot .caption { color: #888; font-size: 0.8rem; margin-top: 0.25rem; }
  .col-extracted { display: flex; flex-direction: column; gap: 1rem; min-width: 0; }
  .extract-block { border: 1px solid #444; border-radius: 0.25rem; background: #1e1e1e; padding: 0.75rem 1rem; max-height: 600px; overflow: auto; }
  .extract-block h3 { margin: 0 0 0.5rem; font-size: 0.85rem; text-transform: uppercase; color: #888; letter-spacing: 0.05em; }
  .extract-block.html-output iframe { width: 100%; height: 480px; border: 0; background: white; border-radius: 0.25rem; }
  .extract-block.text-output pre { white-space: pre-wrap; word-wrap: break-word; font-family: ui-monospace, "SF Mono", Menlo, monospace; font-size: 0.85rem; line-height: 1.5; color: #ddd; margin: 0; }
  .legend { background: #2a2a2a; border-left: 4px solid #4a90e2; padding: 0.75rem 1rem; border-radius: 0.25rem; margin-bottom: 2rem; font-size: 0.9rem; line-height: 1.5; color: #bbb; }
  .legend strong { color: #fff; }
  /* Hebrew/Arabic prose flows RTL; math (LaTeX, numbers in parens) stays
     LTR via <bdi dir="ltr"> when we render via the SPA renderer. The
     pdftotext output here is plain text inside <pre>, so let the OS
     bidi algorithm handle direction natively. */
</style>
</head>
<body>
  <h1>PDF extraction validation — $PDF_BASENAME</h1>
  <div class="meta">$PAGES pages · rendered $(date +%Y-%m-%d) · poppler-utils baseline (pdftotext + pdftohtml)</div>

  <div class="legend">
    <strong>Path A (left column):</strong> The page rendered as a PNG at 150 DPI — this is what the vision-LLM extractor "sees" in the OCR-as-photos path.<br>
    <strong>Path B (top-right):</strong> Structured HTML from <code>pdftohtml -c</code> — text positioned at its original (x, y) coordinates. Read this to judge whether the PDF has a real text layer worth extracting directly (vs needing OCR).<br>
    <strong>Path C (bottom-right):</strong> Plain text from <code>pdftotext -layout</code> — one column per page, layout preserved best-effort. The simplest sanity-check for "does the text layer round-trip cleanly?"<br>
  </div>
EOF

for ((p=1; p<=PAGES; p++)); do
  PNG_NAME=$(printf "page-%d.png" "$p")
  HTML_NAME=$(printf "page-%d.html" "$p")
  TEXT_NAME="page-$p.txt"

  TEXT_CONTENT=""
  if [[ -f "$OUT/text/$TEXT_NAME" ]]; then
    # HTML-escape the text so e.g. &, <, > render correctly.
    TEXT_CONTENT=$(python3 -c "import html, sys; print(html.escape(open(sys.argv[1]).read()))" "$OUT/text/$TEXT_NAME")
  fi

  HTML_FRAME=""
  if [[ -f "$OUT/html/$HTML_NAME" ]]; then
    HTML_FRAME="<iframe src=\"html/$HTML_NAME\"></iframe>"
  else
    HTML_FRAME="<em style=\"color:#777\">No structured HTML emitted for this page.</em>"
  fi

  cat >> "$INDEX" <<EOF
  <div class="page">
    <div class="page-header">Page $p</div>
    <div class="col-snapshot">
      <img src="png/$PNG_NAME" alt="Page $p as PNG @ 150 DPI">
      <div class="caption">A · Rendered snapshot (input to vision-LLM)</div>
    </div>
    <div class="col-extracted">
      <div class="extract-block html-output">
        <h3>B · Structured HTML (pdftohtml -c)</h3>
        $HTML_FRAME
      </div>
      <div class="extract-block text-output">
        <h3>C · Layout-preserved text (pdftotext -layout)</h3>
        <pre>$TEXT_CONTENT</pre>
      </div>
    </div>
  </div>
EOF
done

cat >> "$INDEX" <<EOF
</body>
</html>
EOF

echo "Done. Open: $INDEX"
