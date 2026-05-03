# RDY-076 — F10: Peer study group with DPIA addendum

- **Wave**: D (blocked on DPIA + moderator model)
- **Priority**: LOW (deferred pending compliance)
- **Effort**: 5 engineer-weeks + 2 legal/DPIA weeks (parallel)
- **Dependencies**: DPIA addendum approved; two-sided consent flow; moderator model designed
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F10 (Ran's block)

## Problem

Amir's WhatsApp study group is the current substitute. Peer-learning research (Vygotsky ZPD, peer tutoring) shows real effect when designed right. But Ran blocked F10 in panel review: mastery data visible cross-student is an ICO Children's Code concern, and children's-data-to-children requires explicit affirmative consent from both parties + guardians.

## Scope

**Pre-requisites (before any build)**:
1. DPIA addendum covering (a) peer mastery visibility, (b) anonymous help-request routing, (c) opt-out revocation
2. Two-sided consent flow: each member + their guardian must affirm
3. Moderator model: what happens when member leaves, is suspended, or reports abuse?

**Feature (post-approval)**:
- Invite-only groups, max 6 members
- Topic-level mastery visible to group members (NEVER item-level)
- Anonymous help-request routing: member posts "stuck on X," system anonymously routes to a member who solved it, responder consents per request
- Zero rankings, zero "top student" callouts (GD-004)

**Minimal chat** (Dr. Lior's rule): do not duplicate WhatsApp; only in-context help messaging, not general chat.

## Files to Create / Modify

Do NOT start code until:
- [ ] DPIA addendum signed off by DPO (tracked FIND-privacy-014 per parental-consent.md)
- [ ] Two-sided consent UX designed and reviewed by Ran
- [ ] Moderator model documented

Then:
- `src/shared/Cena.Domain/StudyGroups/StudyGroupAggregate.cs`
- `src/shared/Cena.Domain/StudyGroups/TwoSidedConsent.cs`
- `src/student/full-version/src/views/groups/StudyGroupHome.vue`
- `docs/compliance/dpia-addendum-study-groups.md` — the blocker

## Acceptance Criteria

- [ ] DPIA addendum approved BEFORE any production code merged
- [ ] Two-sided consent: each member affirmed + guardian affirmed, audited
- [ ] Item-level mastery never visible cross-student (topic-level only)
- [ ] Zero ranking / zero top-student surface
- [ ] Leave-group flow tested; data retention on leave = immediate deletion from group context
- [ ] Moderator model documented + implemented (abuse reporting path)

## Success Metrics

- **Group participation rate**: target ≥ 40% of students in eligible cohort
- **Help-request → answer rate**: target ≥ 60%
- **Abuse reports**: target < 1 per 1000 active members per month
- **Retention delta (group vs no-group)**: measure but do not force conclusion

## ADR Alignment

- ADR-0003: cross-student data visibility requires elevated consent + separate retention
- ICO Children's Code: explicit affirmative consent + parental approval
- GD-004: no ranking, no streaks, no FOMO

## Out of Scope

- General chat (duplicate of WhatsApp)
- Cross-group discovery (invite-only)
- Teacher-moderated groups (different consent model; separate task under Wave B if demanded)

## Assignee

Unassigned; Ran leads DPIA; Dr. Nadia leads peer-learning design; hold build until blockers clear.
