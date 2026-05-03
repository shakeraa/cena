# Audit: Orphaned Cross-Lens Handoffs

Total orphaned handoffs: **118**.

**Criteria**: `cross_lens_handoff` from persona A to persona B about a doc where persona B's YAML for that same doc has no finding whose signature shares 3+ content words with the handoff topic. "Absent" means B's YAML did not cover the doc at all; "present-but-unrelated" means it covered the doc but nothing matches the topic.

---

### H-001: Scheduler service boundaries; strategy-discrimination storage model
- **From**: persona-a11y in `persona-a11y/axis1_pedagogy_mechanics_cena.yaml:L85`
- **To**: persona-enterprise (expected in `axis1_pedagogy_mechanics_cena.md`)
- **Original concern**: "Scheduler service boundaries; strategy-discrimination storage model"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-002: F5 calibration gains effect sizes
- **From**: persona-a11y in `persona-a11y/axis2_motivation_self_regulation_findings.yaml:L98`
- **To**: persona-cogsci (expected in `axis2_motivation_self_regulation_findings.md`)
- **Original concern**: "F5 calibration gains effect sizes"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-003: Differential privacy parameters and 30-day enforcement
- **From**: persona-a11y in `persona-a11y/axis9_data_privacy_trust_mechanics.yaml:L115`
- **To**: persona-privacy (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Differential privacy parameters and 30-day enforcement"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-004: Audit trail storage architecture and tenant isolation
- **From**: persona-a11y in `persona-a11y/axis9_data_privacy_trust_mechanics.yaml:L117`
- **To**: persona-enterprise (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Audit trail storage architecture and tenant isolation"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-005: Transparency report publication cadence and SLO
- **From**: persona-a11y in `persona-a11y/axis9_data_privacy_trust_mechanics.yaml:L119`
- **To**: persona-sre (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Transparency report publication cadence and SLO"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-006: F1 SSO token handling and session replay
- **From**: persona-a11y in `persona-a11y/axis_10_operational_integration_features.yaml:L126`
- **To**: persona-redteam (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "F1 SSO token handling and session replay"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-007: Multi-language pipeline for SMS and push; glossary storage and tenant scoping
- **From**: persona-a11y in `persona-a11y/axis_4_parent_engagement_cena_research.yaml:L127`
- **To**: persona-enterprise (expected in `axis_4_parent_engagement_cena_research.md`)
- **Original concern**: "Multi-language pipeline for SMS and push; glossary storage and tenant scoping"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-008: Two-sided consent DPIA and 24h auto-delete enforcement
- **From**: persona-a11y in `persona-a11y/axis_7_collaboration_social_features_cena.yaml:L118`
- **To**: persona-privacy (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "Two-sided consent DPIA and 24h auto-delete enforcement"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-009: Anonymity guarantees in F1/F4 — de-anonymization via voice recognition
- **From**: persona-a11y in `persona-a11y/axis_7_collaboration_social_features_cena.yaml:L122`
- **To**: persona-redteam (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "Anonymity guarantees in F1/F4 — de-anonymization via voice recognition"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-010: F6 translanguaging — language profiles must not persist on student records
- **From**: persona-a11y in `persona-a11y/axis_8_content_authoring_quality_research.yaml:L127`
- **To**: persona-privacy (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "F6 translanguaging — language profiles must not persist on student records"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-011: Per-country Arabic notation profile data model
- **From**: persona-a11y in `persona-a11y/axis_8_content_authoring_quality_research.yaml:L129`
- **To**: persona-enterprise (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "Per-country Arabic notation profile data model"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-012: Teacher workflow tenant boundaries and cross-school data sharing
- **From**: persona-a11y in `persona-a11y/cena_axis5_teacher_workflow_features.yaml:L96`
- **To**: persona-enterprise (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Teacher workflow tenant boundaries and cross-school data sharing"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-013: Observability for teacher feature adoption
- **From**: persona-a11y in `persona-a11y/cena_axis5_teacher_workflow_features.yaml:L98`
- **To**: persona-sre (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Observability for teacher feature adoption"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-014: F10 Arabic math normalizer service boundary (ArabicMathNormalizer.cs)
- **From**: persona-a11y in `persona-a11y/cena_competitive_analysis.yaml:L155`
- **To**: persona-enterprise (expected in `cena_competitive_analysis.md`)
- **Original concern**: "F10 Arabic math normalizer service boundary (ArabicMathNormalizer.cs)"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-015: F2 breathing ritual vs therapy-claim line (Dr. Nadia flag)
- **From**: persona-a11y in `persona-a11y/cena_cross_domain_feature_innovation.yaml:L114`
- **To**: persona-ethics (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F2 breathing ritual vs therapy-claim line (Dr. Nadia flag)"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-016: Full pedagogical review (FD-001 through FD-020)
- **From**: persona-a11y in `persona-a11y/cena_dr_nadia_pedagogical_review_20_findings.yaml:L82`
- **To**: persona-educator (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "Full pedagogical review (FD-001 through FD-020)"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-017: Dedup architecture across FD-014 / AXIS 3 F7 / AXIS 8 F4
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L139`
- **To**: persona-enterprise (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Dedup architecture across FD-014 / AXIS 3 F7 / AXIS 8 F4"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-018: FD-001 through FD-020 pedagogical validity (mostly covered by Nadia)
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L141`
- **To**: persona-educator (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "FD-001 through FD-020 pedagogical validity (mostly covered by Nadia)"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-019: SLO for a11y scanner coverage in CI
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L149`
- **To**: persona-sre (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "SLO for a11y scanner coverage in CI"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-020: FD-017 therapy-claim line and FD-018 energy tracker
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L151`
- **To**: persona-ethics (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "FD-017 therapy-claim line and FD-018 energy tracker"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-021: FD-003 session-scope enforcement
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L153`
- **To**: persona-redteam (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "FD-003 session-scope enforcement"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-022: All effect-size interpretations
- **From**: persona-a11y in `persona-a11y/feature-discovery-2026-04-20.yaml:L155`
- **To**: persona-cogsci (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "All effect-size interpretations"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-023: Full statistical assessment of all 20 findings
- **From**: persona-a11y in `persona-a11y/finding_assessment_dr_rami.yaml:L45`
- **To**: persona-educator (expected in `finding_assessment_dr_rami.md`)
- **Original concern**: "Full statistical assessment of all 20 findings"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-024: F3/F4 retained graded submissions + rubric results; confirm retention policy matches COPPA/PPL.
- **From**: persona-cogsci in `persona-cogsci/AXIS_6_Assessment_Feedback_Research.yaml:L237`
- **To**: persona-privacy (expected in `axis_6_assessment_feedback_research.md`)
- **Original concern**: "F3/F4 retained graded submissions + rubric results; confirm retention policy matches COPPA/PPL."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-025: F1 voice: voice-biometric deanonymisation risk; F4 anonymous Q&A: harassment attack vectors.
- **From**: persona-cogsci in `persona-cogsci/AXIS_7_Collaboration_Social_Features_Cena.yaml:L179`
- **To**: persona-redteam (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F1 voice: voice-biometric deanonymisation risk; F4 anonymous Q&A: harassment attack vectors."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-026: F2 distractor generation — can student infer correct answer from distractor structure?
- **From**: persona-cogsci in `persona-cogsci/AXIS_8_Content_Authoring_Quality_Research.yaml:L187`
- **To**: persona-redteam (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "F2 distractor generation — can student infer correct answer from distractor structure?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-027: F5 privacy copy must meet WCAG + plain-language + RTL requirements.
- **From**: persona-cogsci in `persona-cogsci/axis9_data_privacy_trust_mechanics.yaml:L146`
- **To**: persona-a11y (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "F5 privacy copy must meet WCAG + plain-language + RTL requirements."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-028: F7 DP cohort insights framing risk (see axis4 F4 parallel).
- **From**: persona-cogsci in `persona-cogsci/axis9_data_privacy_trust_mechanics.yaml:L148`
- **To**: persona-ethics (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "F7 DP cohort insights framing risk (see axis4 F4 parallel)."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-029: F8 readiness traffic-light stereotype-threat risk.
- **From**: persona-cogsci in `persona-cogsci/cena_axis5_teacher_workflow_features.yaml:L193`
- **To**: persona-ethics (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "F8 readiness traffic-light stereotype-threat risk."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-030: F5 conversational bot LLM-call volume — if shipped.
- **From**: persona-cogsci in `persona-cogsci/cena_cross_domain_feature_innovation.yaml:L218`
- **To**: persona-finops (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F5 conversational bot LLM-call volume — if shipped."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-031: FD-014 XAI scope constraint (concur amplify Dr. Nadia).
- **From**: persona-cogsci in `persona-cogsci/cena_dr_nadia_pedagogical_review_20_findings.yaml:L151`
- **To**: persona-privacy (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "FD-014 XAI scope constraint (concur amplify Dr. Nadia)."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-032: FD-008 AI grading bypass vectors; FD-002 CAS-gated variation distractor leaks.
- **From**: persona-cogsci in `persona-cogsci/feature-discovery-2026-04-20.yaml:L203`
- **To**: persona-redteam (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "FD-008 AI grading bypass vectors; FD-002 CAS-gated variation distractor leaks."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-033: Webhook signing, OAuth token theft, tenant-escape on CSV export.
- **From**: persona-enterprise in `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L137`
- **To**: persona-redteam (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Webhook signing, OAuth token theft, tenant-escape on CSV export."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-034: Mashov data shape + consent matrix for SIS integration.
- **From**: persona-enterprise in `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L139`
- **To**: persona-privacy (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Mashov data shape + consent matrix for SIS integration."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-035: Integration failure modes, rate-limit behavior, runbook per integration.
- **From**: persona-enterprise in `persona-enterprise/AXIS_10_Operational_Integration_Features.yaml:L141`
- **To**: persona-sre (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Integration failure modes, rate-limit behavior, runbook per integration."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-036: Minor-to-minor social interaction consent matrix.
- **From**: persona-enterprise in `persona-enterprise/AXIS_7_Collaboration_Social_Features_Cena.yaml:L119`
- **To**: persona-privacy (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "Minor-to-minor social interaction consent matrix."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-037: Pedagogical soundness of CAS-generated variants.
- **From**: persona-enterprise in `persona-enterprise/AXIS_8_Content_Authoring_Quality_Research.yaml:L138`
- **To**: persona-educator (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "Pedagogical soundness of CAS-generated variants."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-038: Author identity in provenance — handle vs personal name.
- **From**: persona-enterprise in `persona-enterprise/AXIS_8_Content_Authoring_Quality_Research.yaml:L140`
- **To**: persona-privacy (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "Author identity in provenance — handle vs personal name."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-039: Pedagogical appropriateness of "hint governor" vs. current F9 hint design.
- **From**: persona-enterprise in `persona-enterprise/axis1_pedagogy_mechanics_cena.yaml:L153`
- **To**: persona-educator (expected in `axis1_pedagogy_mechanics_cena.md`)
- **Original concern**: "Pedagogical appropriateness of "hint governor" vs. current F9 hint design."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-040: Banned-term exposure via CurrentStreak — even if unrendered, presence in API payloads is a leak risk.
- **From**: persona-enterprise in `persona-enterprise/axis2_motivation_self_regulation_findings.yaml:L119`
- **To**: persona-ethics (expected in `axis2_motivation_self_regulation_findings.md`)
- **Original concern**: "Banned-term exposure via CurrentStreak — even if unrendered, presence in API payloads is a leak risk."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-041: WCAG 2.2 AA detail and RTL/bdi implementation correctness.
- **From**: persona-enterprise in `persona-enterprise/axis3_accessibility_accommodations_findings.yaml:L105`
- **To**: persona-a11y (expected in `axis3_accessibility_accommodations_findings.md`)
- **Original concern**: "WCAG 2.2 AA detail and RTL/bdi implementation correctness."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-042: Deletion/rebuild runbook + RTO/RPO implications.
- **From**: persona-enterprise in `persona-enterprise/axis9_data_privacy_trust_mechanics.yaml:L138`
- **To**: persona-sre (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Deletion/rebuild runbook + RTO/RPO implications."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-043: PII guard bypasses via tool calls, images, and multi-turn leaks.
- **From**: persona-enterprise in `persona-enterprise/axis9_data_privacy_trust_mechanics.yaml:L140`
- **To**: persona-redteam (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "PII guard bypasses via tool calls, images, and multi-turn leaks."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-044: Competitive comparison for Ministry partnership pitch.
- **From**: persona-enterprise in `persona-enterprise/cena_competitive_analysis.yaml:L76`
- **To**: persona-ministry (expected in `cena_competitive_analysis.md`)
- **Original concern**: "Competitive comparison for Ministry partnership pitch."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-045: Cost-structure benchmarks vs competitors.
- **From**: persona-enterprise in `persona-enterprise/cena_competitive_analysis.yaml:L78`
- **To**: persona-finops (expected in `cena_competitive_analysis.md`)
- **Original concern**: "Cost-structure benchmarks vs competitors."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-046: Evidence quality of competitor claims.
- **From**: persona-enterprise in `persona-enterprise/cena_competitive_analysis.yaml:L80`
- **To**: persona-cogsci (expected in `cena_competitive_analysis.md`)
- **Original concern**: "Evidence quality of competitor claims."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-047: Every cross-domain idea needs GD-004 review.
- **From**: persona-enterprise in `persona-enterprise/cena_cross_domain_feature_innovation.yaml:L78`
- **To**: persona-ethics (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Every cross-domain idea needs GD-004 review."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-048: Cross-domain analogies rarely carry empirical transfer; evidence required before adoption.
- **From**: persona-enterprise in `persona-enterprise/cena_cross_domain_feature_innovation.yaml:L80`
- **To**: persona-cogsci (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Cross-domain analogies rarely carry empirical transfer; evidence required before adoption."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-049: All pedagogical verdicts.
- **From**: persona-enterprise in `persona-enterprise/cena_dr_nadia_pedagogical_review_20_findings.yaml:L64`
- **To**: persona-educator (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "All pedagogical verdicts."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-050: Evidence quality behind verdicts.
- **From**: persona-enterprise in `persona-enterprise/cena_dr_nadia_pedagogical_review_20_findings.yaml:L66`
- **To**: persona-cogsci (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "Evidence quality behind verdicts."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-051: REJECTs that protect GD-004 mechanics.
- **From**: persona-enterprise in `persona-enterprise/cena_dr_nadia_pedagogical_review_20_findings.yaml:L68`
- **To**: persona-ethics (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "REJECTs that protect GD-004 mechanics."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-052: Cost envelope of adopting 18 SHIPs concurrently.
- **From**: persona-enterprise in `persona-enterprise/feature-discovery-2026-04-20.yaml:L128`
- **To**: persona-finops (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Cost envelope of adopting 18 SHIPs concurrently."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-053: Rollout cadence + projection rebuild windows.
- **From**: persona-enterprise in `persona-enterprise/feature-discovery-2026-04-20.yaml:L130`
- **To**: persona-sre (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Rollout cadence + projection rebuild windows."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-054: BORDERLINE features that touch minor data.
- **From**: persona-enterprise in `persona-enterprise/feature-discovery-2026-04-20.yaml:L132`
- **To**: persona-privacy (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "BORDERLINE features that touch minor data."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-055: Effect-size integrity evaluation.
- **From**: persona-enterprise in `persona-enterprise/finding_assessment_dr_rami.yaml:L56`
- **To**: persona-cogsci (expected in `finding_assessment_dr_rami.md`)
- **Original concern**: "Effect-size integrity evaluation."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-056: F2 Web Audio / haptic API — verify no fingerprinting vector for minors
- **From**: persona-ethics in `persona-ethics/axis3_accessibility_accommodations_findings.yaml:L171`
- **To**: persona-redteam (expected in `axis3_accessibility_accommodations_findings.md`)
- **Original concern**: "F2 Web Audio / haptic API — verify no fingerprinting vector for minors"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-057: F7 differential privacy budget ledger design
- **From**: persona-ethics in `persona-ethics/axis9_data_privacy_trust_mechanics.yaml:L161`
- **To**: persona-enterprise (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "F7 differential privacy budget ledger design"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-058: F2 transparency dashboard API — attack surface for data enumeration
- **From**: persona-ethics in `persona-ethics/axis9_data_privacy_trust_mechanics.yaml:L163`
- **To**: persona-redteam (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "F2 transparency dashboard API — attack surface for data enumeration"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-059: F4 CRDT/optimistic sync architecture, F5 multi-device session reconciliation
- **From**: persona-ethics in `persona-ethics/axis_10_operational_integration_features.yaml:L208`
- **To**: persona-enterprise (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "F4 CRDT/optimistic sync architecture, F5 multi-device session reconciliation"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-060: F1 voice recording DPIA, F4 anonymity-to-everyone teacher deanon protocol, F5 differential privacy ε=1.0 justification
- **From**: persona-ethics in `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L314`
- **To**: persona-privacy (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F1 voice recording DPIA, F4 anonymity-to-everyone teacher deanon protocol, F5 differential privacy ε=1.0 justification"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-061: F3 Team Challenges WebSocket + real-time scoring engine SLOs and rollback
- **From**: persona-ethics in `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L318`
- **To**: persona-sre (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F3 Team Challenges WebSocket + real-time scoring engine SLOs and rollback"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-062: F7 whiteboard gesture-based manipulation — motor accessibility
- **From**: persona-ethics in `persona-ethics/axis_7_collaboration_social_features_cena.yaml:L320`
- **To**: persona-a11y (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F7 whiteboard gesture-based manipulation — motor accessibility"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-063: F5 ES=1.16 cherry-picking concern, F1 Koenig 2025 misattribution
- **From**: persona-ethics in `persona-ethics/axis_8_content_authoring_quality_research.yaml:L206`
- **To**: persona-cogsci (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "F5 ES=1.16 cherry-picking concern, F1 Koenig 2025 misattribution"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-064: F7 mood data architectural scope lock + DPIA
- **From**: persona-ethics in `persona-ethics/cena_cross_domain_feature_innovation.yaml:L282`
- **To**: persona-privacy (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F7 mood data architectural scope lock + DPIA"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-065: F3 animated character path motion-reduction compliance
- **From**: persona-ethics in `persona-ethics/cena_cross_domain_feature_innovation.yaml:L284`
- **To**: persona-a11y (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F3 animated character path motion-reduction compliance"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-066: F6 Discord-style anonymous posting moderation edge cases
- **From**: persona-ethics in `persona-ethics/cena_cross_domain_feature_innovation.yaml:L286`
- **To**: persona-redteam (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F6 Discord-style anonymous posting moderation edge cases"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-067: Classroom-practice applicability of each FD-XXX (Miriam lens)
- **From**: persona-ethics in `persona-ethics/cena_dr_nadia_pedagogical_review_20_findings.yaml:L121`
- **To**: persona-educator (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "Classroom-practice applicability of each FD-XXX (Miriam lens)"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-068: 55 findings — primary pedagogy lens
- **From**: persona-ethics in `persona-ethics/feature-discovery-2026-04-20.yaml:L175`
- **To**: persona-cogsci (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "55 findings — primary pedagogy lens"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-069: FD-053 Offline mode, FD-055 Multi-device continuity — SLOs and rollback
- **From**: persona-ethics in `persona-ethics/feature-discovery-2026-04-20.yaml:L179`
- **To**: persona-sre (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "FD-053 Offline mode, FD-055 Multi-device continuity — SLOs and rollback"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-070: Validate F6 SpokenMath vocabulary coverage against WCAG 2.2 AA and SRE library defaults.
- **From**: persona-finops in `persona-finops/axis3_accessibility_accommodations_findings.yaml:L132`
- **To**: persona-a11y (expected in `axis3_accessibility_accommodations_findings.md`)
- **Original concern**: "Validate F6 SpokenMath vocabulary coverage against WCAG 2.2 AA and SRE library defaults."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-071: F7 epsilon budget sizing (is 1/month/tenant right, or finer granularity needed?)
- **From**: persona-finops in `persona-finops/axis9_data_privacy_trust_mechanics.yaml:L144`
- **To**: persona-privacy (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "F7 epsilon budget sizing (is 1/month/tenant right, or finer granularity needed?)"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-072: F4 reconnect storm — confirm observability + rollback plan.
- **From**: persona-finops in `persona-finops/axis_10_operational_integration_features.yaml:L158`
- **To**: persona-sre (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "F4 reconnect storm — confirm observability + rollback plan."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-073: Mashov API resilience patterns + vendor SLA handling.
- **From**: persona-finops in `persona-finops/axis_10_operational_integration_features.yaml:L160`
- **To**: persona-enterprise (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Mashov API resilience patterns + vendor SLA handling."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-074: F1 voice-clip adversarial content — what's the post-filter audit path?
- **From**: persona-finops in `persona-finops/axis_7_collaboration_social_features_cena.yaml:L148`
- **To**: persona-redteam (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F1 voice-clip adversarial content — what's the post-filter audit path?"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-075: F1 voice clip 24h retention + transcription PII exposure to local Whisper.
- **From**: persona-finops in `persona-finops/axis_7_collaboration_social_features_cena.yaml:L152`
- **To**: persona-privacy (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "F1 voice clip 24h retention + transcription PII exposure to local Whisper."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-076: Is 1000 variants/cohort enough coverage, or does sparsity hurt learning?
- **From**: persona-finops in `persona-finops/axis_8_content_authoring_quality_research.yaml:L165`
- **To**: persona-cogsci (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "Is 1000 variants/cohort enough coverage, or does sparsity hurt learning?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-077: Mashov/Classroom integration error-budget and vendor SLA.
- **From**: persona-finops in `persona-finops/cena_axis5_teacher_workflow_features.yaml:L135`
- **To**: persona-enterprise (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Mashov/Classroom integration error-budget and vendor SLA."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-078: Is Haiku quality sufficient for Socratic tutoring, or does the pedagogy require Sonnet?
- **From**: persona-finops in `persona-finops/cena_competitive_analysis.yaml:L176`
- **To**: persona-cogsci (expected in `cena_competitive_analysis.md`)
- **Original concern**: "Is Haiku quality sufficient for Socratic tutoring, or does the pedagogy require Sonnet?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-079: F1 daily-cap exhaustion UX: what does the student see when minutes run out?
- **From**: persona-finops in `persona-finops/cena_competitive_analysis.yaml:L178`
- **To**: persona-sre (expected in `cena_competitive_analysis.md`)
- **Original concern**: "F1 daily-cap exhaustion UX: what does the student see when minutes run out?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-080: F3 'journeys' UX — ensure no streak/loss mechanics, just progress.
- **From**: persona-finops in `persona-finops/cena_cross_domain_feature_innovation.yaml:L102`
- **To**: persona-ethics (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "F3 'journeys' UX — ensure no streak/loss mechanics, just progress."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-081: Bagrut concept graph structure — does prof panel approve?
- **From**: persona-finops in `persona-finops/cena_cross_domain_feature_innovation.yaml:L104`
- **To**: persona-educator (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Bagrut concept graph structure — does prof panel approve?"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-082: FD-008 DPA §7 concern — is human-in-loop mitigation sufficient?
- **From**: persona-finops in `persona-finops/cena_dr_nadia_pedagogical_review_20_findings.yaml:L109`
- **To**: persona-privacy (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "FD-008 DPA §7 concern — is human-in-loop mitigation sufficient?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-083: Cost-exhaustion DoS: can a single student burn through global cap via repeated tutor asks?
- **From**: persona-finops in `persona-finops/feature-discovery-2026-04-20.yaml:L237`
- **To**: persona-redteam (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Cost-exhaustion DoS: can a single student burn through global cap via repeated tutor asks?"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-084: Mashov integration aggregate + credential storage; offline store domain model
- **From**: persona-redteam in `persona-redteam/AXIS_10_Operational_Integration_Features.yaml:L217`
- **To**: persona-enterprise (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Mashov integration aggregate + credential storage; offline store domain model"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-085: Offline data minimization policy
- **From**: persona-redteam in `persona-redteam/AXIS_10_Operational_Integration_Features.yaml:L221`
- **To**: persona-privacy (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "Offline data minimization policy"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-086: SMS spend runaway monitoring for F6
- **From**: persona-redteam in `persona-redteam/AXIS_4_Parent_Engagement_Cena_Research.yaml:L156`
- **To**: persona-sre (expected in `axis_4_parent_engagement_cena_research.md`)
- **Original concern**: "SMS spend runaway monitoring for F6"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-087: F6 misconception store retention enforcement
- **From**: persona-redteam in `persona-redteam/AXIS_6_Assessment_Feedback_Research.yaml:L164`
- **To**: persona-privacy (expected in `axis_6_assessment_feedback_research.md`)
- **Original concern**: "F6 misconception store retention enforcement"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-088: FD-008 fabricated citation follow-up
- **From**: persona-redteam in `persona-redteam/AXIS_6_Assessment_Feedback_Research.yaml:L166`
- **To**: persona-cogsci (expected in `axis_6_assessment_feedback_research.md`)
- **Original concern**: "FD-008 fabricated citation follow-up"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-089: New PyMC/Stan sidecar network boundary design
- **From**: persona-redteam in `persona-redteam/AXIS_8_Content_Authoring_Quality_Research.yaml:L163`
- **To**: persona-enterprise (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "New PyMC/Stan sidecar network boundary design"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-090: F5 cultural-context risk in Bagrut-aligned materials
- **From**: persona-redteam in `persona-redteam/AXIS_8_Content_Authoring_Quality_Research.yaml:L165`
- **To**: persona-ministry (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "F5 cultural-context risk in Bagrut-aligned materials"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-091: F5 stereotype-amplification risk
- **From**: persona-redteam in `persona-redteam/AXIS_8_Content_Authoring_Quality_Research.yaml:L167`
- **To**: persona-ethics (expected in `axis_8_content_authoring_quality_research.md`)
- **Original concern**: "F5 stereotype-amplification risk"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-092: F3 "wise feedback" wording — confirm not crossing into persuasion/coercion
- **From**: persona-redteam in `persona-redteam/axis2_motivation_self_regulation_findings.yaml:L131`
- **To**: persona-ethics (expected in `axis2_motivation_self_regulation_findings.md`)
- **Original concern**: "F3 "wise feedback" wording — confirm not crossing into persuasion/coercion"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-093: Layla owns the actual RTL / MathML correctness; red-team only flags the attack surface
- **From**: persona-redteam in `persona-redteam/axis3_accessibility_accommodations_findings.yaml:L120`
- **To**: persona-a11y (expected in `axis3_accessibility_accommodations_findings.md`)
- **Original concern**: "Layla owns the actual RTL / MathML correctness; red-team only flags the attack surface"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-094: DP epsilon budget design for F7
- **From**: persona-redteam in `persona-redteam/axis9_data_privacy_trust_mechanics.yaml:L166`
- **To**: persona-privacy (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "DP epsilon budget design for F7"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-095: Provenance trail storage SLO + retention policy
- **From**: persona-redteam in `persona-redteam/axis9_data_privacy_trust_mechanics.yaml:L168`
- **To**: persona-sre (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Provenance trail storage SLO + retention policy"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-096: Data inventory aggregate modeling
- **From**: persona-redteam in `persona-redteam/axis9_data_privacy_trust_mechanics.yaml:L170`
- **To**: persona-enterprise (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Data inventory aggregate modeling"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-097: Minimum-cell-size policy — exact k value, k-anonymity vs l-diversity choice
- **From**: persona-redteam in `persona-redteam/cena_axis5_teacher_workflow_features.yaml:L154`
- **To**: persona-privacy (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Minimum-cell-size policy — exact k value, k-anonymity vs l-diversity choice"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-098: Extending ClassMasteryHeatmapProjection vs creating parallel projections
- **From**: persona-redteam in `persona-redteam/cena_axis5_teacher_workflow_features.yaml:L158`
- **To**: persona-enterprise (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Extending ClassMasteryHeatmapProjection vs creating parallel projections"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-099: Portfolio retention (F7) + at-risk alert design (F4)
- **From**: persona-redteam in `persona-redteam/cena_competitive_analysis.yaml:L133`
- **To**: persona-privacy (expected in `cena_competitive_analysis.md`)
- **Original concern**: "Portfolio retention (F7) + at-risk alert design (F4)"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-100: FD-009 Khanmigo evidence (already in Dr. Rami)
- **From**: persona-redteam in `persona-redteam/cena_competitive_analysis.yaml:L135`
- **To**: persona-cogsci (expected in `cena_competitive_analysis.md`)
- **Original concern**: "FD-009 Khanmigo evidence (already in Dr. Rami)"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-101: Mood-check frequency and wellbeing claims
- **From**: persona-redteam in `persona-redteam/cena_cross_domain_feature_innovation.yaml:L129`
- **To**: persona-cogsci (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Mood-check frequency and wellbeing claims"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-102: FD-016 competition framing
- **From**: persona-redteam in `persona-redteam/cena_dr_nadia_pedagogical_review_20_findings.yaml:L90`
- **To**: persona-ethics (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "FD-016 competition framing"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-103: Primitives (1)(6) + retention inventory
- **From**: persona-redteam in `persona-redteam/feature-discovery-2026-04-20.yaml:L150`
- **To**: persona-privacy (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Primitives (1)(6) + retention inventory"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-104: Primitive (2) outbound sanitizer needs observable cost + error surface
- **From**: persona-redteam in `persona-redteam/feature-discovery-2026-04-20.yaml:L152`
- **To**: persona-sre (expected in `feature-discovery-2026-04-20.md`)
- **Original concern**: "Primitive (2) outbound sanitizer needs observable cost + error surface"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-105: FD-003 + FD-008 data protection implications
- **From**: persona-redteam in `persona-redteam/finding_assessment_dr_rami.yaml:L111`
- **To**: persona-privacy (expected in `finding_assessment_dr_rami.md`)
- **Original concern**: "FD-003 + FD-008 data protection implications"
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-106: Evidence provenance is Kenji's lens; I only flag it as a governance red-team concern
- **From**: persona-redteam in `persona-redteam/finding_assessment_dr_rami.yaml:L113`
- **To**: persona-cogsci (expected in `finding_assessment_dr_rami.md`)
- **Original concern**: "Evidence provenance is Kenji's lens; I only flag it as a governance red-team concern"
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-107: Affective signal handling per ADR-0037.
- **From**: persona-sre in `persona-sre/axis2_motivation_self_regulation_findings.yaml:L104`
- **To**: persona-privacy (expected in `axis2_motivation_self_regulation_findings.md`)
- **Original concern**: "Affective signal handling per ADR-0037."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-108: Lead on consent + erasure specification; SRE follows their binding decisions.
- **From**: persona-sre in `persona-sre/axis9_data_privacy_trust_mechanics.yaml:L116`
- **To**: persona-privacy (expected in `axis9_data_privacy_trust_mechanics.md`)
- **Original concern**: "Lead on consent + erasure specification; SRE follows their binding decisions."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-109: PPA Amendment 13 data portability export scope.
- **From**: persona-sre in `persona-sre/axis_10_operational_integration_features.yaml:L141`
- **To**: persona-privacy (expected in `axis_10_operational_integration_features.md`)
- **Original concern**: "PPA Amendment 13 data portability export scope."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-110: Parent-teacher messaging abuse surface.
- **From**: persona-sre in `persona-sre/axis_4_parent_engagement_cena_research.yaml:L106`
- **To**: persona-redteam (expected in `axis_4_parent_engagement_cena_research.md`)
- **Original concern**: "Parent-teacher messaging abuse surface."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-111: Misconception-tag store (shared primitive with FD-003).
- **From**: persona-sre in `persona-sre/axis_6_assessment_feedback_research.yaml:L113`
- **To**: persona-privacy (expected in `axis_6_assessment_feedback_research.md`)
- **Original concern**: "Misconception-tag store (shared primitive with FD-003)."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-112: Abuse + collusion threat model for Features 4, 6, 7.
- **From**: persona-sre in `persona-sre/axis_7_collaboration_social_features_cena.yaml:L101`
- **To**: persona-redteam (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "Abuse + collusion threat model for Features 4, 6, 7."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-113: Voice recording retention + transcript handling under COPPA + PPL.
- **From**: persona-sre in `persona-sre/axis_7_collaboration_social_features_cena.yaml:L103`
- **To**: persona-privacy (expected in `axis_7_collaboration_social_features_cena.md`)
- **Original concern**: "Voice recording retention + transcript handling under COPPA + PPL."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-114: Small-class suppression policy as a PPL compliance control.
- **From**: persona-sre in `persona-sre/cena_axis5_teacher_workflow_features.yaml:L103`
- **To**: persona-privacy (expected in `cena_axis5_teacher_workflow_features.md`)
- **Original concern**: "Small-class suppression policy as a PPL compliance control."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-115: Whether knowledge graph pedagogy is a real differentiator or cosmetic.
- **From**: persona-sre in `persona-sre/cena_cross_domain_feature_innovation.yaml:L78`
- **To**: persona-educator (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Whether knowledge graph pedagogy is a real differentiator or cosmetic."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-116: Graph rendering cost per dashboard load.
- **From**: persona-sre in `persona-sre/cena_cross_domain_feature_innovation.yaml:L80`
- **To**: persona-finops (expected in `cena_cross_domain_feature_innovation.md`)
- **Original concern**: "Graph rendering cost per dashboard load."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

### H-117: Warning-to-rule translation correctness.
- **From**: persona-sre in `persona-sre/cena_dr_nadia_pedagogical_review_20_findings.yaml:L74`
- **To**: persona-educator (expected in `cena_dr_nadia_pedagogical_review_20_findings.md`)
- **Original concern**: "Warning-to-rule translation correctness."
- **B's YAML status**: absent
- **Suggested resolution**: new-task-in-B-lens

### H-118: Marketing copy honesty (FD-006 null-result disclosure).
- **From**: persona-sre in `persona-sre/finding_assessment_dr_rami.yaml:L95`
- **To**: persona-ethics (expected in `finding_assessment_dr_rami.md`)
- **Original concern**: "Marketing copy honesty (FD-006 null-result disclosure)."
- **B's YAML status**: present-but-unrelated
- **Suggested resolution**: confirm-handled-elsewhere-or-add-to-existing-prr

