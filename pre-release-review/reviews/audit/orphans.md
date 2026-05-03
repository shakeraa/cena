# Audit: Orphaned Findings

Total orphans: **141**.

**Criteria**: a raw `extracted_tasks:` entry from a persona YAML whose title words (after stopword filter) do not share 4+ content words with any task in `tasks.jsonl` AND share no referenced code file / doc-line / task with tasks.jsonl, AND is not covered by an entry in `retired.md` AND is not deferred in `conflicts.md`.

**Note**: matching is fuzzy on first-N content words. An orphan can still be "false-orphan" if renaming flattened the title; review recommended.

---

### O-001: Add explicit aria-label for emoji-based confidence check-ins (F5)
- **Source**: `persona-a11y/axis2_motivation_self_regulation_findings.yaml:L77`
- **Lens**: persona-a11y
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-002: Introduce chart-only color-blind palette without touching primary #7367F0
- **Source**: `persona-a11y/axis3_accessibility_accommodations_findings.yaml:L163`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-003: Fix amber-on-white contrast in proposed CalmAlert
- **Source**: `persona-a11y/axis3_accessibility_accommodations_findings.yaml:L182`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-004: Pin reading-level target for privacy copy in he/ar/en
- **Source**: `persona-a11y/axis9_data_privacy_trust_mechanics.yaml:L63`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-005: SSO button + greeting a11y/bidi contract (F1)
- **Source**: `persona-a11y/axis_10_operational_integration_features.yaml:L69`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-006: Decision-gate: locale scope for parent portal (he/ar/en vs +ru/am)
- **Source**: `persona-a11y/axis_4_parent_engagement_cena_research.yaml:L72`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-007: Bidi-isolation rule for SMS content (no <bdi> in plain text)
- **Source**: `persona-a11y/axis_4_parent_engagement_cena_research.yaml:L88`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-008: Shape/pattern differentiation for readiness/error dashboards
- **Source**: `persona-a11y/axis_6_assessment_feedback_research.yaml:L64`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-009: aria-live='polite' for F6 real-time misconception tags
- **Source**: `persona-a11y/axis_6_assessment_feedback_research.yaml:L97`
- **Lens**: persona-a11y
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-010: F7 whiteboard dual-modality: structured math input alongside canvas
- **Source**: `persona-a11y/axis_7_collaboration_social_features_cena.yaml:L82`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-011: Dedup F4 Arabic RTL against AXIS 3 F7 into single roadmap
- **Source**: `persona-a11y/axis_8_content_authoring_quality_research.yaml:L72`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-012: ADR + POC: TTS voice quality for Hebrew/Arabic math
- **Source**: `persona-a11y/cena_competitive_analysis.yaml:L75`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-013: Virtual manipulative (F5) keyboard+SR accessibility contract
- **Source**: `persona-a11y/cena_competitive_analysis.yaml:L129`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-014: Concept map (F1) accessibility equivalent: keyboard-navigable text list
- **Source**: `persona-a11y/cena_cross_domain_feature_innovation.yaml:L63`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-015: Animation policy ADR + reduced-motion compliance for F2/F3/F4
- **Source**: `persona-a11y/cena_cross_domain_feature_innovation.yaml:L80`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-016: Localize cross-domain feature copy in he/ar/en
- **Source**: `persona-a11y/cena_cross_domain_feature_innovation.yaml:L95`
- **Lens**: persona-a11y
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-017: Translation review gate for therapy/medical language (FD-017)
- **Source**: `persona-a11y/cena_dr_nadia_pedagogical_review_20_findings.yaml:L63`
- **Lens**: persona-a11y
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-018: Upgrade FD-016 MathML screen reader from SHORTLIST to SHIP
- **Source**: `persona-a11y/feature-discovery-2026-04-20.yaml:L91`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-019: Add a11y-scanner-coverage-for-admin FD (new)
- **Source**: `persona-a11y/feature-discovery-2026-04-20.yaml:L106`
- **Lens**: persona-a11y
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-020: Add 'Per-paragraph lang attribute for multilingual content' FD (new)
- **Source**: `persona-a11y/feature-discovery-2026-04-20.yaml:L121`
- **Lens**: persona-a11y
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-021: Ship F5 Weekly 3-2-1 Snapshot using existing progress metrics only
- **Source**: `persona-cogsci/AXIS_4_Parent_Engagement_Cena_Research.yaml:L97`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-022: Ship F5 'I'm Confused Too' anonymous signal
- **Source**: `persona-cogsci/AXIS_7_Collaboration_Social_Features_Cena.yaml:L105`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-023: Pre-problem retrieval prompts (F3) as session-local low-stakes recall
- **Source**: `persona-cogsci/axis1_pedagogy_mechanics_cena.yaml:L157`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-024: Ship F2 If-Then Session Planner reading onboarding affective tags
- **Source**: `persona-cogsci/axis2_motivation_self_regulation_findings.yaml:L122`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-025: Retire F8 Reflective Study Plan Generator citation of Hattie d=1.44
- **Source**: `persona-cogsci/axis2_motivation_self_regulation_findings.yaml:L148`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-026: F1 Socratic SE prompt — enforce Redis-only storage for responses
- **Source**: `persona-cogsci/axis2_motivation_self_regulation_findings.yaml:L194`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-027: Promote F8 Calm UI from feature to design-system lint rules
- **Source**: `persona-cogsci/axis3_accessibility_accommodations_findings.yaml:L113`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-028: F1 XAI: ship rule-based explanation templates, not personalized cogtrait XAI
- **Source**: `persona-cogsci/axis9_data_privacy_trust_mechanics.yaml:L92`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-029: Ship F3 In-Class Diagnostic Sprint (session-aggregated histogram)
- **Source**: `persona-cogsci/cena_axis5_teacher_workflow_features.yaml:L138`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-030: F4 Exit Ticket: pair every 'you might review Y' with 1-tap practice
- **Source**: `persona-cogsci/cena_axis5_teacher_workflow_features.yaml:L159`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-031: Features 1-3: polish existing stuck-type ontology UX, do not build new hint system
- **Source**: `persona-cogsci/cena_competitive_analysis.yaml:L133`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-032: Elevate Dr. Nadia's FD-009 Socratic revisions to mandatory DoD for any Socratic ticket
- **Source**: `persona-cogsci/cena_dr_nadia_pedagogical_review_20_findings.yaml:L121`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-033: Re-bucket 'Top-10 Quick Wins' by evidence-quality tier
- **Source**: `persona-cogsci/feature-discovery-2026-04-20.yaml:L161`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-034: SYNTHESIS: propagate Dr. Rami's 3 REJECTs across all axis docs
- **Source**: `persona-cogsci/finding_assessment_dr_rami.yaml:L107`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-035: SYNTHESIS: standard framing fix for FD-001 interleaving d-range
- **Source**: `persona-cogsci/finding_assessment_dr_rami.yaml:L130`
- **Lens**: persona-cogsci
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-036: Offline mode restricts to untimed practice (no summative)
- **Source**: `persona-educator/AXIS_10_Operational_Integration_Features.yaml:L126`
- **Lens**: persona-educator
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-037: Constrain weekly 3-2-1 'support at home' suggestions to non-math
- **Source**: `persona-educator/AXIS_4_Parent_Engagement_Cena_Research.yaml:L176`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-038: Reframe confidence calibration as insight, not score penalty
- **Source**: `persona-educator/AXIS_6_Assessment_Feedback_Research.yaml:L210`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-039: Ship I'm-Confused-Too anonymous class-period signal
- **Source**: `persona-educator/AXIS_7_Collaboration_Social_Features_Cena.yaml:L119`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-040: Default teacher-micro-group moderation to post-hoc + flagged alerts
- **Source**: `persona-educator/AXIS_7_Collaboration_Social_Features_Cena.yaml:L161`
- **Lens**: persona-educator
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-041: Add sentence-starter surface to ExplainItBack for L-D/Arabic students
- **Source**: `persona-educator/axis2_motivation_self_regulation_findings.yaml:L141`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-042: Build if-then implementation-intentions planner (F2) with preview reminders
- **Source**: `persona-educator/axis2_motivation_self_regulation_findings.yaml:L163`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-043: Wise-feedback template library keyed by error type + locale
- **Source**: `persona-educator/axis2_motivation_self_regulation_findings.yaml:L188`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-044: Throttle confidence check-ins to ≤1 per 5 problems, high-leverage only
- **Source**: `persona-educator/axis2_motivation_self_regulation_findings.yaml:L211`
- **Lens**: persona-educator
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-045: Scope 'Why this problem' to ≤15-word single-sentence header
- **Source**: `persona-educator/axis9_data_privacy_trust_mechanics.yaml:L100`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-046: Architectural test: provenance trail never leaks misconception tags
- **Source**: `persona-educator/axis9_data_privacy_trust_mechanics.yaml:L121`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-047: Hide differentiation stream labels from student view
- **Source**: `persona-educator/cena_axis5_teacher_workflow_features.yaml:L186`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-048: Learning-journeys design constraint: no days-missed indicator
- **Source**: `persona-educator/cena_cross_domain_feature_innovation.yaml:L165`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-049: Focus-ritual mood-adjustment copy: 'familiar pattern' not 'easier'
- **Source**: `persona-educator/cena_cross_domain_feature_innovation.yaml:L185`
- **Lens**: persona-educator
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-050: Pairing-requirement ADR enforcing Nadia's dependency matrix
- **Source**: `persona-educator/cena_dr_nadia_pedagogical_review_20_findings.yaml:L142`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-051: Add 'already-built coverage' annotation to feature-discovery index
- **Source**: `persona-educator/feature-discovery-2026-04-20.yaml:L133`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-052: Resolve all 8 BORDERLINE findings to explicit ADRs before queue entry
- **Source**: `persona-educator/feature-discovery-2026-04-20.yaml:L152`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-053: Reframe FD-006 CBM as metacognitive tool, update success metrics
- **Source**: `persona-educator/finding_assessment_dr_rami.yaml:L150`
- **Lens**: persona-educator
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-054: Design webhook delivery context
- **Source**: `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L72`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-055: Secret/token storage ADR + skeleton
- **Source**: `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L92`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-056: Fix TZ infra before calendar features
- **Source**: `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L111`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-057: Define tier-routing policy for translation pipeline
- **Source**: `persona-enterprise/AXIS_4_Parent_Engagement_Cena_Research.yaml:L109`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-058: Wire explanatory feedback through tier router
- **Source**: `persona-enterprise/AXIS_6_Assessment_Feedback_Research.yaml:L113`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-059: Fail-closed guard on SymPy sidecar
- **Source**: `persona-enterprise/AXIS_8_Content_Authoring_Quality_Research.yaml:L92`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-060: Author provenance schema unification
- **Source**: `persona-enterprise/AXIS_8_Content_Authoring_Quality_Research.yaml:L113`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-061: Publish upcaster convention doc for pedagogy events
- **Source**: `persona-enterprise/axis1_pedagogy_mechanics_cena.yaml:L102`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-062: Design ConsentAggregate + events
- **Source**: `persona-enterprise/axis9_data_privacy_trust_mechanics.yaml:L111`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-063: Add idempotency key to classroom enrollment creation
- **Source**: `persona-enterprise/cena_axis5_teacher_workflow_features.yaml:L90`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-064: Retire 'new heatmap' proposal — extend existing projection
- **Source**: `persona-enterprise/cena_axis5_teacher_workflow_features.yaml:L112`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-065: Translate Nadia's SHIP-with-caveat findings into schema impact list
- **Source**: `persona-enterprise/cena_dr_nadia_pedagogical_review_20_findings.yaml:L41`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-066: Re-label quick-wins list with infra dependencies
- **Source**: `persona-enterprise/feature-discovery-2026-04-20.yaml:L105`
- **Lens**: persona-enterprise
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-067: Student-facing 'go gentler today' control for difficulty adjuster
- **Source**: `persona-ethics/axis1_pedagogy_mechanics_cena.yaml:L155`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-068: User research: Arab-Israeli review of 'wise feedback' persona templates
- **Source**: `persona-ethics/axis2_motivation_self_regulation_findings.yaml:L146`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-069: Body-doubling mode: ban real-minor recordings, require AI-stock label
- **Source**: `persona-ethics/axis3_accessibility_accommodations_findings.yaml:L101`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-070: Energy tracker: explicit confirm before difficulty adjustment
- **Source**: `persona-ethics/axis3_accessibility_accommodations_findings.yaml:L122`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-071: Remove cooling-off period from data deletion flow
- **Source**: `persona-ethics/axis9_data_privacy_trust_mechanics.yaml:L91`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-072: Transparency report: add banned-mechanic compliance section
- **Source**: `persona-ethics/axis9_data_privacy_trust_mechanics.yaml:L132`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-073: Cohort-context copy lockdown: positive-frame only
- **Source**: `persona-ethics/axis_4_parent_engagement_cena_research.yaml:L204`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-074: Misconception tag UI: diagnostic-offer framing, dismissible
- **Source**: `persona-ethics/axis_6_assessment_feedback_research.yaml:L186`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-075: F3 Team Challenges: remove confetti, pyramid-collapse framing, tier comparisons
- **Source**: `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L181`
- **Lens**: persona-ethics
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-076: F4 Q&A: remove helpfulness tokens; replace with simple thanks
- **Source**: `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L208`
- **Lens**: persona-ethics
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-077: F2 watermark: session-id not student-id
- **Source**: `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L289`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-078: F7 peer-comparison: remove selective-display-when-flattering logic
- **Source**: `persona-ethics/axis_8_content_authoring_quality_research.yaml:L113`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-079: F6 explicit ban on language-proficiency inference
- **Source**: `persona-ethics/axis_8_content_authoring_quality_research.yaml:L159`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-080: F3 rubric review: 'how this scored' framing, not 'lost points'
- **Source**: `persona-ethics/axis_8_content_authoring_quality_research.yaml:L179`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-081: Exit Ticket student-share copy lockdown (no comparisons, no medians)
- **Source**: `persona-ethics/cena_axis5_teacher_workflow_features.yaml:L117`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-082: Student-preview requirement for conference prep briefs
- **Source**: `persona-ethics/cena_axis5_teacher_workflow_features.yaml:L137`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-083: Gate F8 'what-if' projections behind dept-head activation
- **Source**: `persona-ethics/cena_axis5_teacher_workflow_features.yaml:L159`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-084: Competitive doc Feature 7: rename 'practice streaks' -> 'consistency calendar'
- **Source**: `persona-ethics/cena_competitive_analysis.yaml:L99`
- **Lens**: persona-ethics
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-085: Retire idle-pulse animation in Stuck? Ask button
- **Source**: `persona-ethics/cena_competitive_analysis.yaml:L168`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-086: Ban reward-inflation emoji (🔥, ⚡) in learning UI
- **Source**: `persona-ethics/cena_cross_domain_feature_innovation.yaml:L179`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-087: F7 Learning Energy Tracker: architectural scope lock + PPA review
- **Source**: `persona-ethics/cena_cross_domain_feature_innovation.yaml:L198`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-088: F2 breathing opener: confirm-before-route on mood tap
- **Source**: `persona-ethics/cena_cross_domain_feature_innovation.yaml:L222`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-089: Journey path: no animation, no sound, pause-friendly copy
- **Source**: `persona-ethics/cena_cross_domain_feature_innovation.yaml:L238`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-090: Session type menu: rename 'Challenge Round'
- **Source**: `persona-ethics/cena_cross_domain_feature_innovation.yaml:L256`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-091: Formalize 'intrinsic motivation risk map' as design-review tool
- **Source**: `persona-ethics/cena_dr_nadia_pedagogical_review_20_findings.yaml:L88`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-092: Add ethics reviewer to future discovery-cycle cross-examination
- **Source**: `persona-ethics/feature-discovery-2026-04-20.yaml:L101`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-093: Re-audit 55 findings BORDERLINE count with ethics lens
- **Source**: `persona-ethics/feature-discovery-2026-04-20.yaml:L125`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-094: Top-10 Shortlist: carry-forward critique annotation
- **Source**: `persona-ethics/feature-discovery-2026-04-20.yaml:L148`
- **Lens**: persona-ethics
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-095: Feature-spec citation verifiability rule
- **Source**: `persona-ethics/finding_assessment_dr_rami.yaml:L94`
- **Lens**: persona-ethics
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-096: Ship static i18n bundle for Wise Feedback phrases (zero runtime LLM)
- **Source**: `persona-finops/axis2_motivation_self_regulation_findings.yaml:L106`
- **Lens**: persona-finops
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-097: Generate problem rationale at selection time (not view time), cache per state
- **Source**: `persona-finops/axis9_data_privacy_trust_mechanics.yaml:L86`
- **Lens**: persona-finops
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-098: Ship Google SSO (Firebase provider wiring)
- **Source**: `persona-finops/axis_10_operational_integration_features.yaml:L128`
- **Lens**: persona-finops
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-099: Use local Whisper tiny for peer-clip transcription (zero runtime API cost)
- **Source**: `persona-finops/axis_7_collaboration_social_features_cena.yaml:L116`
- **Lens**: persona-finops
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-100: Batch-generate 1000 culturally-contextualized problem variants per cohort
- **Source**: `persona-finops/axis_8_content_authoring_quality_research.yaml:L107`
- **Lens**: persona-finops
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-101: Pre-author Bagrut concept-map graph JSON (one-time authoring)
- **Source**: `persona-finops/cena_cross_domain_feature_innovation.yaml:L71`
- **Lens**: persona-finops
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-102: Expand Batch API (50% discount) to all authoring-time generation jobs
- **Source**: `persona-finops/feature-discovery-2026-04-20.yaml:L199`
- **Lens**: persona-finops
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-103: Honest ES in product copy (Interleaving d=0.34, not 0.5-0.8)
- **Source**: `persona-finops/finding_assessment_dr_rami.yaml:L121`
- **Lens**: persona-finops
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-104: Clamp adaptive scheduler sampling to student BagrutTrack
- **Source**: `persona-ministry/axis1_pedagogy_mechanics_cena.yaml:L112`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-105: Add Bagrut-chapter-aligned transfer map for F7 near-to-far transfer
- **Source**: `persona-ministry/axis1_pedagogy_mechanics_cena.yaml:L138`
- **Lens**: persona-ministry
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-106: Bind reflective-study-plan generator to StudentAdvancement prereq DAG
- **Source**: `persona-ministry/axis2_motivation_self_regulation_findings.yaml:L106`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-107: Remove "grade-point lift" claims from wise-feedback copy
- **Source**: `persona-ministry/axis2_motivation_self_regulation_findings.yaml:L132`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-108: Author accommodations-parity ADR (Ministry exam alignment)
- **Source**: `persona-ministry/axis3_accessibility_accommodations_findings.yaml:L110`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-109: Widen F8 transparency report to include curriculum-fidelity metrics
- **Source**: `persona-ministry/axis9_data_privacy_trust_mechanics.yaml:L108`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-110: Mashov legal-posture doc + kill-switch
- **Source**: `persona-ministry/axis_10_operational_integration_features.yaml:L159`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-111: Require Ministry chapter name references in "Why This Topic" templates
- **Source**: `persona-ministry/axis_4_parent_engagement_cena_research.yaml:L141`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-112: Bind lesson-planner output to ChapterDocument prereq DAG
- **Source**: `persona-ministry/cena_axis5_teacher_workflow_features.yaml:L138`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-113: Bind Math Concept Map to canonical taxonomy + prereq DAG
- **Source**: `persona-ministry/cena_cross_domain_feature_innovation.yaml:L93`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-114: Elevate BORDERLINE RDY-080 mitigations from UI copy to architecture tests
- **Source**: `persona-ministry/feature-discovery-2026-04-20.yaml:L140`
- **Lens**: persona-ministry
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-115: Label remediation content (FD-011, FD-012) as below-Bagrut scope
- **Source**: `persona-ministry/feature-discovery-2026-04-20.yaml:L199`
- **Lens**: persona-ministry
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-116: Retention window for summative attempts (Feature 1)
- **Source**: `persona-privacy/AXIS_6_Assessment_Feedback_Research.yaml:L182`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-117: Ban voice-capture for under-16 + biometric consent for 16+ (Feature 1)
- **Source**: `persona-privacy/AXIS_7_Collaboration_Social_Features_Cena.yaml:L111`
- **Lens**: persona-privacy
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-118: Classify cultural/ethnic cohort tag as GDPR Art 9 special-category
- **Source**: `persona-privacy/AXIS_8_Content_Authoring_Quality_Research.yaml:L126`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-119: Consolidate self-efficacy / confidence profile storage (Axis 8 F7 + Axis 6 F8)
- **Source**: `persona-privacy/AXIS_8_Content_Authoring_Quality_Research.yaml:L149`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-120: Add new per-student projections to erasure cascade
- **Source**: `persona-privacy/axis1_pedagogy_mechanics_cena.yaml:L150`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-121: Classify accommodation data as GDPR Art 9 special-category + encrypt at rest
- **Source**: `persona-privacy/axis3_accessibility_accommodations_findings.yaml:L111`
- **Lens**: persona-privacy
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-122: Align Feature 1 retention to ADR-0003 windows (30/90) not 90-day
- **Source**: `persona-privacy/axis3_accessibility_accommodations_findings.yaml:L141`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-123: Reconcile dashboard visibility with export ([MlExcluded] consistency)
- **Source**: `persona-privacy/axis9_data_privacy_trust_mechanics.yaml:L204`
- **Lens**: persona-privacy
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-124: Split ageBand enum: under13 / 13-15 / 16-17 / 18+ for consent defaults
- **Source**: `persona-privacy/axis9_data_privacy_trust_mechanics.yaml:L228`
- **Lens**: persona-privacy
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-125: Confirm Anthropic ZDR for tutor messages + update privacy page
- **Source**: `persona-privacy/cena_competitive_analysis.yaml:L113`
- **Lens**: persona-privacy
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-126: Offline cache encryption + wipe-on-logout
- **Source**: `persona-redteam/AXIS_10_Operational_Integration_Features.yaml:L175`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-127: F4 free-response grader must refuse Unverifiable CAS bindings
- **Source**: `persona-redteam/AXIS_6_Assessment_Feedback_Research.yaml:L119`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-128: Drop F7 whiteboard free-draw until real-time moderation exists
- **Source**: `persona-redteam/AXIS_7_Collaboration_Social_Features_Cena.yaml:L125`
- **Lens**: persona-redteam
- **Priority (as raised)**: P0
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task (priority P0 — surface for coordinator sign-off)

### O-129: Forbid SVG uploads in student-authored sketch endpoints (PNG raster only)
- **Source**: `persona-redteam/axis3_accessibility_accommodations_findings.yaml:L77`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-130: Scope-guard test template applied to every new teacher endpoint
- **Source**: `persona-redteam/cena_axis5_teacher_workflow_features.yaml:L131`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-131: Expand TutorSafetyGuard answer-leak patterns + add long-numeric filter
- **Source**: `persona-redteam/cena_competitive_analysis.yaml:L108`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-132: Apply URL/PII sanitizer to F6 Notes + F8 Collection content
- **Source**: `persona-redteam/cena_cross_domain_feature_innovation.yaml:L101`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-133: Synthesis note — combine Dr. Nadia PASS + Dr. Rami WARN/REJECT verdicts
- **Source**: `persona-redteam/cena_dr_nadia_pedagogical_review_20_findings.yaml:L63`
- **Lens**: persona-redteam
- **Priority (as raised)**: P2
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-134: Bound Bagrut-readiness outputs to ordinal categories (no numerics)
- **Source**: `persona-redteam/finding_assessment_dr_rami.yaml:L86`
- **Lens**: persona-redteam
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-135: Extend shipgate scanner to cover the axis-2 motivation vocabulary
- **Source**: `persona-sre/axis2_motivation_self_regulation_findings.yaml:L81`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-136: Add RTL <bdi dir="ltr"> regression guard to a11y CI
- **Source**: `persona-sre/axis3_accessibility_accommodations_findings.yaml:L61`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-137: Model capacity impact of 10-minute ADHD sessions (SignalR fan-out, session starts/sec)
- **Source**: `persona-sre/axis3_accessibility_accommodations_findings.yaml:L77`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-138: Publish math-glossary assertions into translation-qa CI
- **Source**: `persona-sre/axis_4_parent_engagement_cena_research.yaml:L83`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-139: Add ephemeral-store deletion probe for 24h-retention claim
- **Source**: `persona-sre/axis_7_collaboration_social_features_cena.yaml:L82`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-140: Load-test in-class diagnostic sprint hub at 50 classes x 30 students
- **Source**: `persona-sre/cena_axis5_teacher_workflow_features.yaml:L81`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

### O-141: Encode Dr Nadia's "Required Guardrails" as shipgate CI rules per feature
- **Source**: `persona-sre/cena_dr_nadia_pedagogical_review_20_findings.yaml:L50`
- **Lens**: persona-sre
- **Priority (as raised)**: P1
- **Why it's orphaned**: no fuzzy title/file/doc-line match in tasks.jsonl; not present in retired.md or conflicts.md
- **Suggested resolution**: new-task

