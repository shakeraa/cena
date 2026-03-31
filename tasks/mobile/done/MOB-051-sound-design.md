# MOB-051: Sound Design System

**Priority:** P5.2 — Medium
**Phase:** 5 — Polish & Delight (Months 12-15)
**Source:** microinteractions-emotional-design.md Section 7
**Blocked by:** MOB-050 (Celebration System)
**Estimated effort:** M (1-3 weeks)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** See `tasks/00-master-plan.md`.

## Subtasks

### MOB-051.1: Sound Palette (12 Effects)
- [ ] `correct_chime`: C major arpeggio, 300ms, warm
- [ ] `wrong_gentle`: soft descending tone, 200ms, not punishing
- [ ] `streak_milestone`: ascending fanfare, 800ms
- [ ] `badge_unlock`: magical sparkle + chime, 600ms
- [ ] `level_up`: triumphant brass, 1200ms
- [ ] `quest_complete`: adventure horn, 500ms
- [ ] `hint_reveal`: subtle whoosh, 150ms
- [ ] `session_start`: gentle welcome tone, 400ms
- [ ] `session_complete`: satisfying completion chord, 600ms
- [ ] `nav_tap`: soft click, 50ms (subtle)
- [ ] `xp_count`: rapid tick (for XP counting animation), 30ms per tick
- [ ] `timer_warning`: gentle pulse, 200ms (opt-in timer only)

### MOB-051.2: Ambient Study Sounds (Optional)
- [ ] Lo-fi study beats
- [ ] Rain sounds
- [ ] Library ambience
- [ ] Silence (default)
- [ ] Volume control independent of effect sounds

### MOB-051.3: Sound Settings
- [ ] Master toggle: sounds on/off
- [ ] Separate: effects / ambient / haptics toggles
- [ ] Respect device silent mode
- [ ] Age-appropriate: no childish sounds for 15-18 year olds

### MOB-051.4: Implementation
- [ ] `audioplayers` package for sound effects
- [ ] Pre-load all effects into audio pool on app start
- [ ] Max concurrent sounds: 3 (prevent audio clutter)

**Definition of Done:**
- 12 sound effects with coordinated haptics
- Ambient study sounds option
- Respects silent mode and user preferences
- Pre-loaded for zero-latency playback

**Test:**
```dart
test('Sound system respects silent mode', () {
  final soundSystem = CenaSoundSystem(silentMode: true);
  soundSystem.play(CenaSound.correctChime);
  expect(soundSystem.lastPlayedSound, isNull); // Nothing played
});
```
