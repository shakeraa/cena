# Ship-gate positive-test fixture: reward-inflation emoji (prr-153)

**Purpose**: This file deliberately contains every emoji pattern in
`scripts/shipgate/reward-inflation-emoji.yml`. The CI test suite
(`tests/shipgate/reward-inflation-emoji.spec.mjs`) runs the scanner
against this fixture and asserts that EVERY rule fires at least once.

**Do not clean this file.** Do not rephrase. Each line below is a trap.
This file is whitelisted in
`scripts/shipgate/reward-inflation-emoji-whitelist.yml`.

---

## prr-153 — reward-inflation emoji (BANNED in student learning UI)

- Great job! 🔥 keep going
- You nailed that 🔥
- Fast work! ⚡ very quick
- Lightning round! ⚡
- 100% complete 💯
- Perfect score 💯
- Unit complete 🏆
- Cohort winner 🏆
- Streak hit (banned by GD-004 separately) 🎉
- Celebration bar 🎉
- Star performer 🌟
- Top of the class 🌟
- Good work ⭐
- Earned a star ⭐

## Mixed line (flags multiple rules at once)

- 🔥⚡💯🏆🎉🌟⭐ — every reward emoji in one burst.

## Context lines (still banned)

- "title": "Free Trial 🎉"
- "message": "Congratulations! 🏆 You earned 100 XP 💯"
- "badge": "⭐ Mastered"
- "toast": "Streak day 5 🔥"
