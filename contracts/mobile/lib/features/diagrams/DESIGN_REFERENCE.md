# Interactive Diagram Design Reference

## Inspiration Sources (from user screenshots)

### 1. SmartyMe Physics — Circuit Cards
- Color-coded cards (red/blue/green/yellow glow borders)
- Real component photos (9V battery, resistors, LEDs, logic gates)
- Formula overlay: `V = IR`, `I = V/R`, `Vt = V1 + V2 + ...`
- Short description per card: "Learn why LEDs need current limiting"
- Game-like progression: each card is a level/challenge
- **Cena adaptation**: Per-concept cards with formula, visual, and one-line challenge

### 2. FigureLabs — AI Scientific Illustrator
- Clean vector illustrations (cell biology, lab equipment, molecular structures)
- Editable PPTX export (text replacement on diagram elements)
- Labeled components with leader lines
- Minimalist flat design with consistent color palette
- **Cena adaptation**: LLM-generated SVG diagrams per concept, cached as Protobuf

### 3. SmartyMe Math — Calculus Progress
- 3D rendered math symbols (limits torus, dy/dx infinity, integral cylinder)
- Progress bars per topic (28%, 62%)
- Dark theme with glowing accents
- "Replace scrolling with addictive calculus lessons"
- **Cena adaptation**: Knowledge graph nodes rendered as 3D-style topic cards with mastery %

### 4. Engineering Mindset — Engineering Game
- Subject grid: Structural, Electrical, Mechanical, Civil, Control Systems
- Hand-drawn technical style (blueprint feel)
- Each subject is a "level" with engineering diagrams
- **Cena adaptation**: Subject picker with technical illustration style per STEM domain

### 5. InstaDoodle — Visual Explanations
- Cartoon professor character + topic illustration
- "Visually Explain Anything" — whiteboard style
- Friendly, approachable, non-intimidating
- **Cena adaptation**: Concept explanations with character-guided visual walkthroughs

## Key Design Principles for Cena Diagrams
1. **Pre-generated and cached** — NOT generated on-the-fly during sessions
2. **Per-concept, per-difficulty** — each concept has 1-3 diagram variants
3. **Interactive hotspots** — tap parts of the diagram to see explanations
4. **RTL Hebrew labels** — all text rendered in Hebrew with LTR math fallback
5. **Consistent subject palette**: Math=blue/teal, Physics=orange/amber, Chemistry=green, Biology=purple
6. **Game-feel progression** — diagrams unlock as mastery increases
7. **Exportable** — students can share/save their knowledge graph as image
