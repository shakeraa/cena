---
audience: student
version: v1.0.0 2026-04-21
effective_from: 2026-04-21
supersedes: privacy-policy-children.md (unversioned draft 2026-04-11)
doc_id: cena-privacy-policy-student-v1.0.0
reading_level: approx-grade-7-through-9 (age 13+ audience; avoids legal jargon while preserving accuracy)
jurisdictions:
  - GDPR (EU) — your rights as a data subject
  - COPPA 2025 Final Rule (US) — referenced for parent-gated items at Under13
  - PPL Amendment 13 (Israel)
status: counsel-review-pending
---

# Your Privacy on Cena — Student Edition

**This page is for you.** If you are 13 or older, the rights here belong
directly to you. If you are under 13, your parent or guardian holds most
of these rights on your behalf; the
[parent edition](privacy-policy-parent.md) explains what they can see and
decide.

## 1. What Cena knows about you

Cena is the app you use for learning. To make it work, Cena stores a small
amount of information:

- **Your name and email** — so you can sign in.
- **Your birthday** — so Cena applies the right rules for your age.
- **Your answers and your progress** — so Cena can pick questions that fit
  you.
- **Your chat with the AI tutor** — so Cena can answer follow-up questions
  and help you learn.
- **A small amount of technical data** — like which browser you used — so
  we can keep the service working and safe.

That is the full list. Cena does not store your location, your contacts,
what other apps you use, or the content of your device beyond what you send
to Cena on purpose.

## 2. What Cena does **not** do

Cena never:

- Sells your information to anyone.
- Shows you ads.
- Uses your data to train general AI models.
- Shares your answers with other students.
- Uses the kinds of pressure tricks that some apps use to push you into
  opening them every day. The research (summarised in our design
  non-negotiables) shows those patterns harm learners, so we refuse to
  build them.

## 3. What you can see

At **13 and up**, Cena shows you a dashboard that mirrors what your parent
sees about you. You can see every item and decide whether to say anything
about it.

At **16 and up**, you can hide most of those items from your parent. You
cannot hide safety items (for example, if Cena detects that you are in
distress). This is by design: it protects you, not the institute.

At **any age**, you can ask Cena:

- What it knows about you (a JSON file you can download).
- To fix something that is wrong.
- To delete your account and everything in it.

## 4. How to change things

- **To see your data**: open Settings → Privacy → "Download a copy of my
  data".
- **To fix something**: Settings → Privacy → "Correct my information".
- **To delete your account**: Settings → Privacy → "Close my account".
- **To hide a category from your parent** (only 16+): Settings → Parent
  view → toggle the category off.

If a button does not do what it says, that is a bug — email
`privacy@cena.example` and we will fix it. If we do not fix it fast enough,
you can file a complaint with your country's privacy regulator. You have
that right; using it does not cost you anything and Cena will not close
your account because you filed.

## 5. How long Cena keeps your data

Different kinds of data live for different amounts of time:

- **Your answers and your progress** — usually kept for as long as your
  school uses Cena, then deleted on a schedule your school sets.
- **Misconception signals** (what Cena notices you are confused about
  right now) — kept for at most **30 days**, then deleted. This is a
  hard rule we wrote into the code.
- **Your tutor chat** — kept for 90 days, or less if you ask.
- **Your account itself** — deleted 30 days after you close it.

When Cena deletes your data, it is not recoverable. The math behind this
is called crypto-shredding: Cena throws away the key that reads your
ciphertext, so nobody — not even Cena's engineers — can read it again.

## 6. Sharing

Cena shares your data with exactly three kinds of people:

1. **Your institute** — your teachers and administrators at the school
   that set you up with Cena. Not other institutes.
2. **Anthropic** — the company that runs the AI model behind the tutor,
   under a contract that says they cannot use your data for their own
   purposes.
3. **A court, only if ordered by a judge** — and only for the narrowest
   part of your data that the order requires. Cena does not volunteer
   anything.

## 7. If you have a problem with Cena

- Email `privacy@cena.example`.
- Tell a trusted adult — your parent, your teacher, or your school's
  privacy officer.
- In the EU, the UK, or Israel, you can file a complaint with your
  country's data-protection authority. In the US, you can file with
  the FTC. The complaint form is on their website.

You do not need to ask for permission to file a complaint. You do not
need to keep using Cena while the complaint is open.

## 8. When this page changes

When Cena updates this page, we do two things:

- We bump the version at the top of the page.
- We ask you to look at the new version the next time you open Cena.

You can read previous versions at `/legal/history` if you want to see
what changed.

## 9. The short version

- Cena keeps the minimum data it needs to help you learn.
- You can see, fix, or delete your data at any time.
- Cena does not sell your data, show you ads, or train general models on it.
- Your parent can see most things until you are 13; after that, your
  rights grow as you do.
- If something feels wrong, ask. You are allowed to ask.

---

## 10. Related documents

- [Privacy policy — parent edition](privacy-policy-parent.md) — the
  detailed version for adults.
- [Terms of service](terms-of-service.md) — the rules of using Cena.

Internal references: ADR-0003 (misconception session-scope retention),
ADR-0038 (right to be forgotten via crypto-shred), ADR-0041 (age-band
matrix for who sees what).
