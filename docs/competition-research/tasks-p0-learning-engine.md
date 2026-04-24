# P0 Learning Engine Tasks

> **Source:** extracted-features.md (A1)
> **Sprint Goal:** Add camera/OCR scan-to-solve — the #1 most requested feature in ed-tech
> **Total Estimate:** 8-12 weeks
> **Expected Impact:** +30-40% user acquisition, +25% engagement, +20% revenue

---

## LRN-001: Camera/OCR Scan-to-Solve
**ROI: 9.2 | Size: L (8-12 weeks) | No dependencies**

### Description
Users point their camera at a math/STEM problem (handwritten or printed) and get instant AI-powered step-by-step solutions. Integrates with CENA's Socratic AI tutor — instead of just showing the answer, CENA guides the student through understanding.

### Why This Matters
- Photomath has 220M+ downloads primarily because of this feature
- #1 most requested feature in App Store reviews across ed-tech
- Camera input is the most natural interaction for mobile-first students

### Build vs Buy Decision
**BUY: Mathpix API** ($0.02-0.05 per scan)
- Building OCR from scratch: 6+ months, $500K+
- Mathpix integration: 4-6 weeks for core, 2-4 weeks for polish
- Alternative: Google ML Kit (free but less accurate for handwriting)
- Recommendation: Start with Mathpix, evaluate Google ML Kit for cost optimization at scale

### Acceptance Criteria

**Camera Capture:**
- [ ] Camera permission flow (graceful denial handling)
- [ ] Real-time viewfinder with problem detection bounding box
- [ ] Support for handwritten text (math, equations, diagrams)
- [ ] Support for printed text (textbooks, worksheets)
- [ ] Photo library import (select existing photo)
- [ ] Crop/adjust tool after capture
- [ ] Flash/torch toggle
- [ ] Multi-problem detection (identify multiple problems in frame)

**OCR Processing:**
- [ ] Send captured image to Mathpix API
- [ ] Parse LaTeX response into structured problem representation
- [ ] Handle recognition errors gracefully ("Couldn't read this, try again")
- [ ] Confidence score display (high/medium/low accuracy indicator)
- [ ] Support for: arithmetic, algebra, geometry, trigonometry, calculus, statistics
- [ ] Support for: word problems (extract text + math)
- [ ] Offline fallback: queue scan for when connectivity returns

**Solution Display:**
- [ ] Step-by-step solution with CENA's Socratic approach (hints first, then reveal)
- [ ] Toggle between "Guide me" (Socratic) and "Show solution" (direct)
- [ ] Multiple solution methods when available (algebraic, graphical, numerical)
- [ ] Interactive graph for relevant problems
- [ ] LaTeX rendering for mathematical notation
- [ ] "Save to review" — add problem to SRS queue for later practice
- [ ] "Similar problems" — generate practice problems of same type

**Integration with CENA:**
- [ ] Scanned problem feeds into AI tutor conversation
- [ ] AI tutor asks "What have you tried?" before showing solution
- [ ] Problem categorized and mapped to knowledge graph node
- [ ] SRS integration: scanned problems appear in future reviews
- [ ] Analytics: scan count, problem types, conversion to study session

**Monetization:**
- [ ] Free tier: 5 scans per day
- [ ] Premium: unlimited scans
- [ ] Scan counter visible with upgrade prompt

### Subtasks
1. Mathpix API integration (auth, image upload, LaTeX response parsing)
2. Camera capture screen (viewfinder, bounding box, flash, crop)
3. Photo library import flow
4. OCR result parsing → structured problem model
5. Error handling UI (retry, manual input fallback)
6. Step-by-step solution renderer (LaTeX, interactive)
7. Socratic mode toggle ("Guide me" vs "Show solution")
8. Multiple solution methods display
9. AI tutor integration (scanned problem → conversation)
10. Knowledge graph mapping (categorize problem to topic node)
11. SRS integration ("Save to review" → FSRS scheduler)
12. "Similar problems" generator
13. Scan counter + free tier limit (5/day)
14. Premium unlimited bypass
15. Offline queue (store scan, process when online)
16. Analytics events: scan_initiated, scan_success, scan_error, scan_to_study, problem_type
17. Integration tests: handwritten, printed, multi-problem, error cases
18. Performance testing: camera startup time < 1s, OCR response < 3s
