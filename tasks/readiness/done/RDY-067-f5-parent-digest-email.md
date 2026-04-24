# RDY-067 — F5a: Weekly parent digest (email only, no WhatsApp yet)

- **Wave**: A
- **Priority**: HIGH
- **Effort**: 2 engineer-weeks
- **Dependencies**: none
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F5; [synthesis](../../docs/research/cena-user-personas-and-features-2026-04-17.md) F5
- **Tier**: 2 (parent trust stack)

## Problem

Rachel (product-manager mother), Mahmoud (Arabic-L1 father), Dalia (LD-advocate mother) all want low-friction parental visibility without surveillance theater. Hebrew-market incumbents offer nothing of the kind. Dr. Lior flagged: parents read 15 seconds, not 2 minutes — first line must be the verdict.

## Scope

Weekly Sunday 20:00 Asia/Jerusalem email digest, per enrolled student, delivered to the consented parent account.

**Digest structure (3-line default, expandable):**

```
[Verdict line]   Amir is on track this week.
[Topic + delta]  Worked on integration by parts; mastery 0.62 → 0.78.
[Next step]      One thing to celebrate: solved 8 derivative chain-rule
                 items first-try. One thing for next week: u-substitution
                 for trig integrands.
```

**Banned content** (CI scanner enforces):
- No streaks, no "X-day record," no loss-aversion language
- No comparative claims ("2 weeks behind the class") — absolute/self-referential only
- No "upgrade to Premium to see more" paywall (Mahmoud's dealbreaker)
- No misconception codes / error-type tags (session-scoped per ADR-0003)

**Compassionate-framing edge case (Rami's challenge):** When a student practiced 0 hours, the digest should say "Amir took a break this week — that's fine. Here's what he can pick up next week." Never "Amir did nothing."

**Language**: render in parent's profile-set language (he / ar / en). Template strings go through i18n.

## Files to Create / Modify

- `src/api/Cena.Admin.Api/Features/ParentDigest/DigestWeeklyProjection.cs` — Marten projection rolling up student week
- `src/api/Cena.Admin.Api/Features/ParentDigest/DigestRenderer.cs` — Liquid/Razor template
- `src/api/Cena.Admin.Api/Features/ParentDigest/DigestDispatcher.cs` — cron-triggered email sender
- `src/shared/Cena.Infrastructure/Email/IEmailSender.cs` + SMTP/SendGrid adapter
- `tests/Cena.Admin.Api.Tests/ParentDigest/ShipgateDigestContentTests.cs` — banned-term scanner against rendered output (per Ran's request)
- `docs/design/parent-digest-design.md`

## Acceptance Criteria

- [ ] Digest rendered for 3 language locales (he, ar, en) with RTL correctness for ar/he
- [ ] Dispatch runs as weekly cron, Sunday 20:00 Asia/Jerusalem
- [ ] Ship-gate scanner passes on rendered output for 50-sample golden set
- [ ] Zero-hours-this-week edge case renders compassionate variant, not "nothing happened"
- [ ] Misconception tags NEVER appear in output (assert in tests)
- [ ] Parent can unsubscribe via one-click link; unsubscribe audited

## Success Metrics

- **Open rate**: target >55% (Mailchimp education benchmark ~40%; ours should beat due to high relevance)
- **Click-to-dashboard rate**: target >10% (parents who want detail will click)
- **Unsubscribe rate**: target <2% (higher = content is wrong)
- **Parent NPS on digest**: target ≥ 40

## ADR Alignment

- ADR-0003: content contains topic + mastery only, never misconception codes (Ran verification in CI)
- GD-004: ship-gate scanner on rendered digest
- Parental consent flow per docs/compliance/parental-consent.md
- Israel PPL + GDPR: unsubscribe must be honored ≤ 30 days

## Out of Scope

- WhatsApp delivery (F5b / RDY-069, Wave B — separate task due to 3-week integration scope)
- SMS delivery (Wave C if needed)
- In-app parent dashboard deep-link — separate task

## Assignee

Unassigned; Oren for plumbing, Dr. Lior for copy, Ran for compliance pass.
