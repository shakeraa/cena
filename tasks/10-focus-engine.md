# 10 — Focus Degradation & Resilience Engine

> **Source:** `docs/focus-degradation-research.md` (20-iteration autoresearch, 2026-03-27)
> **Bounded contexts:** Learner (core), Pedagogy (supporting), Mobile (delivery)
> **Technology:** Proto.Actor .NET 9, Flutter (sensors_plus, battery_plus), SignalR
> **Existing code:** `src/actors/Cena.Actors/Services/FocusDegradationService.cs` (540 lines)

## Overview

The Focus Engine enhances Cena's existing 4-signal focus model with mobile sensor data, proactive microbreaks, emotion discrimination (boredom vs fatigue, confusion vs frustration), and cultural adaptations. Based on 20 research iterations covering 40+ citations across vigilance theory, productive failure, mind-wandering, circadian effects, cognitive load, and affect detection.

## Tasks

| ID | Name | Priority | Effort | Blocked By |
|----|------|----------|--------|------------|
| FOC-001 | Enhanced focus signal pipeline (add sensor signals) | P1 | L | ACT-002, MOB-003 |
| FOC-002 | Mobile sensor data collection layer | P1 | L | MOB-001 |
| FOC-003 | Proactive microbreak engine | P0 | M | FOC-001 |
| FOC-004 | Mind-wandering detector (aware vs unaware) | P2 | M | FOC-001 |
| FOC-005 | Confusion vs frustration discriminator | P1 | M | FOC-001, ACT-004 |
| FOC-006 | Boredom-fatigue splitter | P1 | M | FOC-001, FOC-005 |
| FOC-007 | Chronotype detector & circadian adjuster | P2 | M | FOC-002, DATA-004 |
| FOC-008 | Solution diversity tracker | P1 | S | ACT-003, MST-001 |
| FOC-009 | Sensor privacy layer (on-device, consent) | P0 | M | FOC-002 |
| FOC-010 | Focus A/B testing framework | P2 | M | FOC-003, FOC-006 |
| FOC-011 | Gamification novelty rotation | P2 | S | MOB-008 |
| FOC-012 | Cultural resilience stratification | P3 | S | FOC-001 |

## Dependency Chain (Critical Path)

```
FOC-002 (sensors) → FOC-009 (privacy) → FOC-001 (pipeline) → FOC-003 (microbreaks)
                                              ↓
                                         FOC-005 (confusion) → FOC-006 (boredom)
                                              ↓
                                         FOC-004 (mind-wandering)
FOC-008 (solution diversity) — independent
FOC-007 (chronotype) ← FOC-002
FOC-010 (A/B) ← FOC-003, FOC-006
FOC-011 (gamification) — independent
FOC-012 (cultural) ← FOC-001
```

## Stage Mapping

- **Intelligence (Weeks 9-12):** FOC-002, FOC-009, FOC-001, FOC-003, FOC-008
- **Polish (Weeks 13-16):** FOC-005, FOC-006, FOC-004, FOC-007, FOC-011, FOC-012
- **Launch Prep (Weeks 17-18):** FOC-010

## Mobile Sensor Capabilities Used

| Sensor | iOS | Android | Flutter Package | Signal |
|--------|-----|---------|----------------|--------|
| Accelerometer | CoreMotion | SensorManager | `sensors_plus` | Phone movement, fidgeting, put-down detection |
| Gyroscope | CoreMotion | SensorManager | `sensors_plus` | Device orientation stability |
| Ambient Light | Not exposed* | SensorManager | `light` | Study environment brightness |
| Touch Patterns | UITouch | MotionEvent | Platform channels | Tap rhythm, pressure proxy via area, swipe velocity |
| App Lifecycle | UIApplication | Activity | `WidgetsBindingObserver` | App backgrounding, multitasking detection |
| Battery | UIDevice | BatteryManager | `battery_plus` | Late-night study indicator |
| Proximity | UIDevice.proximityState | SensorManager | `proximity_sensor_plugin` | Face-down detection, in-pocket |
| Screen Time | ScreenTime API** | UsageStatsManager** | Custom plugin | Cross-app distraction patterns |

\* iOS ambient light requires private API or ARKit workaround (light estimation)
\** Screen time data requires user opt-in and has platform restrictions
