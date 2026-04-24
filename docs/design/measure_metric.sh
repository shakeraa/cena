#!/bin/bash
# Autoresearch metric: measures research depth and implementation quality
# Score = citations×2 + resolved×5 + implementations×3 + integrations×3

DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$DIR/../.." && pwd)"

FILE1="$ROOT/docs/design/micro-lessons-design.md"
FILE2="$ROOT/docs/discussion-student-ai-interaction.md"

# 1. Unique citations (Author, Year pattern or Author et al. pattern)
CITATIONS=$(cat "$FILE1" "$FILE2" 2>/dev/null | grep -oE '[A-Z][a-z]+( et al\.)?,? *\(?[12][09][0-9]{2}\)?' | sort -u | wc -l | tr -d ' ')

# 2. Resolved open questions
RESOLVED=$(cat "$FILE1" "$FILE2" 2>/dev/null | grep -ci 'RESOLVED\|✅.*resolved\|ANSWERED')

# 3. Implementation details (code blocks, data models)
IMPLEMENTATIONS=$(cat "$FILE1" "$FILE2" 2>/dev/null | grep -c '```')
IMPLEMENTATIONS=$((IMPLEMENTATIONS / 2)) # pairs of backticks = code blocks

# 4. Cena-specific integration points (actors, services, events, DTOs)
INTEGRATIONS=$(cat "$FILE1" "$FILE2" 2>/dev/null | grep -oE '(Actor|Service|Detector|Cache|Event|Dto|NATS|Proto\.Actor|Marten|BKT|MCM|HLR|FSRS|QuestionPool|StudentActor|LearningSession|Stagnation|FocusState|Methodology|BloomLevel)' | sort -u | wc -l | tr -d ' ')

SCORE=$(( CITATIONS * 2 + RESOLVED * 5 + IMPLEMENTATIONS * 3 + INTEGRATIONS * 3 ))

echo "CITATIONS=$CITATIONS RESOLVED=$RESOLVED IMPLEMENTATIONS=$IMPLEMENTATIONS INTEGRATIONS=$INTEGRATIONS SCORE=$SCORE"
