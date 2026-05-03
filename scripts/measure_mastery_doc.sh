#!/bin/bash
# AUTORESEARCH metric script for docs/mastery-measurement-research.md
# Outputs a composite score — all grep uses -E (extended regex) for macOS compat

FILE="docs/mastery-measurement-research.md"

if [ ! -f "$FILE" ]; then
  echo "ERROR: $FILE not found"
  exit 1
fi

# 1. Count verifiable citations: "Author (Year)" or "Author et al. (Year)" or "Author & Author, Year"
CITATIONS=$(grep -oE '[A-Z][a-z]+( et al\.)?( &| and)? [A-Z]?[a-z]* ?\(?[12][0-9]{3}\)?' "$FILE" | sort -u | wc -l | tr -d ' ')

# 2. Count DOI/arXiv references
DOIS=$(grep -oiE '(doi[:/] *10\.[0-9]{4,}/[^ )]+|arxiv\.org/[^ )]+|arXiv:[0-9]{4}\.[0-9]+)' "$FILE" | sort -u | wc -l | tr -d ' ')

# 3. Count mathematical formulations (lines inside code blocks with math-like patterns)
MATH_FORMULAS=$(grep -cE '^\s*(P\(|R\(|S_|h\s*=|theta|sigmoid|softmax|exp\(|sum\(|product\(|min\(|max\(|sqrt\(|log\(|alpha|beta|lambda|w_[0-9]|SE\(|mastery|recall|accuracy|composite)' "$FILE" | tr -d ' ')

# 4. Count Cena-specific references
CENA_DOMAIN_EVENTS=$(grep -oE '`(ConceptAttempted|ConceptMastered|MasteryDecayed|MethodologySwitched|StagnationDetected|AnnotationAdded)`' "$FILE" | sort -u | wc -l | tr -d ' ')
CENA_ACTORS=$(grep -oiE '(StudentProfile|LearningSession|Proto\.Actor|virtual actor|event.sourc)' "$FILE" | sort -u | wc -l | tr -d ' ')
CENA_TECH=$(grep -oiE '(Neo4j|PostgreSQL|Marten|NATS|JetStream|\.NET|C#|SignalR|pyBKT|node2vec)' "$FILE" | sort -u | wc -l | tr -d ' ')
CENA_SPECIFICS=$((CENA_DOMAIN_EVENTS + CENA_ACTORS + CENA_TECH))

# 5. Count code blocks (pairs)
CODE_BLOCKS_RAW=$(grep -c '```' "$FILE" | tr -d ' ')
CODE_BLOCKS=$((CODE_BLOCKS_RAW / 2))

# 6. Count actionable recommendations
RECOMMENDATIONS=$(grep -ciE '(recommend|cena should|at launch|phase [0-9]|practical|implementat)' "$FILE" | tr -d ' ')

# 7. Count method sections (### headers)
METHOD_SECTIONS=$(grep -c '^###' "$FILE" | tr -d ' ')

# 8. Count known missing methods that SHOULD be present
MISSING=0
for METHOD in "ELO" "Elo" "Performance Factor" "PFA" "FSRS" "SM-2" "SuperMemo" "Leitner" "Zone of Proximal" "ZPD" "scaffolding" "cognitive load theory" "CLT" "interleaving" "desirable difficult" "transfer" "near transfer" "far transfer"; do
  if ! grep -qi "$METHOD" "$FILE" 2>/dev/null; then
    MISSING=$((MISSING + 1))
  fi
done

# Composite score
SCORE=$(( (CITATIONS * 3) + (DOIS * 5) + (MATH_FORMULAS * 2) + (CENA_SPECIFICS * 4) + (CODE_BLOCKS * 2) + (RECOMMENDATIONS * 1) + (METHOD_SECTIONS * 1) - (MISSING * 3) ))

echo "=== MASTERY DOC METRICS ==="
echo "Citations (unique):      $CITATIONS"
echo "DOI/arXiv refs:          $DOIS"
echo "Math formulations:       $MATH_FORMULAS"
echo "Cena domain specifics:   $CENA_SPECIFICS"
echo "Code blocks:             $CODE_BLOCKS"
echo "Recommendations:         $RECOMMENDATIONS"
echo "Method sections:         $METHOD_SECTIONS"
echo "Missing methods:         $MISSING (penalty)"
echo "==========================="
echo "COMPOSITE SCORE:         $SCORE"
echo ""
echo "metric=$SCORE"
