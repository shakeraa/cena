# FOC-002: Mobile Sensor Data Collection Layer

**Priority:** P1 — enables all sensor-enhanced focus signals
**Blocked by:** MOB-001 (Flutter scaffold)
**Estimated effort:** 1-2 weeks
**Contract:** New Flutter service layer

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md`.

## Context

Mobile devices carry accelerometers, gyroscopes, ambient light sensors, proximity sensors, battery info, and rich touch data. This is untapped gold for attention modeling — no biometrics needed, no camera, no microphone. The research (autoresearch Iteration 13, ADHD studies) shows smartwatch-based attention tools achieved 88.9% emotion recognition accuracy. We can get partway there with phone sensors alone.

This task builds the Flutter sensor collection layer that feeds FOC-001's enhanced pipeline. All processing happens ON-DEVICE (privacy-first, see FOC-009). Only aggregated scores (not raw sensor data) leave the device.

## Subtasks

### FOC-002.1: Accelerometer & Gyroscope Signal
**Files:**
- `lib/services/sensors/motion_sensor_service.dart` — NEW
- `pubspec.yaml` — add `sensors_plus: ^6.1.1`

**What it detects:**
- **Phone put-down:** Sudden acceleration spike → stillness = phone placed on table (focused)
- **Fidgeting:** High-frequency low-amplitude motion = student handling phone nervously
- **Walking:** Periodic acceleration pattern = studying while walking (lower focus)
- **Stable hold:** Low motion variance = actively engaged with screen

**Acceptance:**
- [ ] `MotionSensorService` streams `MotionState` every 2 seconds (not per-frame — battery efficient)
- [ ] `MotionState` enum: `StableHold`, `TableRest`, `Fidgeting`, `Walking`, `Unknown`
- [ ] Motion stability score: 0.0 (walking) → 0.5 (fidgeting) → 0.8 (stable hold) → 1.0 (table rest + active touch)
- [ ] Uses `sensors_plus` `accelerometerEventStream()` and `gyroscopeEventStream()`
- [ ] Sampling: raw sensor at 10Hz, windowed analysis every 2s (rolling 20-sample window)
- [ ] Platform-specific: graceful degradation if sensors unavailable (returns `null` score)
- [ ] Battery optimization: stops sensor listening when app is backgrounded

**Test:**
```dart
test('stable hold detected from low-variance accelerometer data', () {
  final samples = List.generate(20, (_) =>
    AccelerometerEvent(0.1, -9.78, 0.05)); // near-still, upright
  final state = MotionAnalyzer.classify(samples);
  expect(state, MotionState.stableHold);
  expect(state.score, greaterThanOrEqualTo(0.7));
});

test('fidgeting detected from high-frequency motion', () {
  final samples = List.generate(20, (i) =>
    AccelerometerEvent(sin(i * 0.5) * 2, -9.78 + cos(i * 0.3), sin(i * 0.7)));
  final state = MotionAnalyzer.classify(samples);
  expect(state, MotionState.fidgeting);
  expect(state.score, lessThan(0.5));
});
```

### FOC-002.2: App Focus Signal (Lifecycle Events)
**Files:**
- `lib/services/sensors/app_focus_service.dart` — NEW

**What it detects:**
- **App backgrounding:** Student switched to another app (Instagram, TikTok, WhatsApp)
- **Quick return:** Backgrounded <5s = notification glance (minor distraction)
- **Extended leave:** Backgrounded >30s = significant attention loss
- **Notification interruptions:** (Android only) notification count during session via NotificationListenerService

**Acceptance:**
- [ ] `AppFocusService` extends `WidgetsBindingObserver`
- [ ] Tracks `backgroundDuration`, `backgroundCount`, `timeSinceLastBackground` per session
- [ ] App focus score: `1.0 - (totalBackgroundSeconds / sessionSeconds).clamp(0, 1)` with exponential decay weighting (recent backgrounds penalized more)
- [ ] Quick glances (<5s) penalized at 50% rate vs extended leaves
- [ ] `AppFocusEvent` record: `timestamp`, `duration`, `wasQuickGlance`
- [ ] Works on iOS AND Android with identical API surface

**Test:**
```dart
test('no backgrounding scores 1.0', () {
  final service = AppFocusService();
  service.startSession();
  // simulate 10 minutes with no backgrounding
  expect(service.computeScore(elapsed: Duration(minutes: 10)), 1.0);
});

test('30s background in 10min session reduces score', () {
  final service = AppFocusService();
  service.startSession();
  service.recordBackground(Duration(seconds: 30));
  final score = service.computeScore(elapsed: Duration(minutes: 10));
  expect(score, lessThan(0.95));
  expect(score, greaterThan(0.80));
});
```

### FOC-002.3: Touch Pattern Signal
**Files:**
- `lib/services/sensors/touch_pattern_service.dart` — NEW

**What it detects:**
- **Tap rhythm consistency:** Focused students have regular tap timing; distracted students are erratic
- **Touch area stability:** Consistent finger contact area = deliberate input; varying = sloppy
- **Swipe velocity:** Panicky fast scrolling = frustration; smooth scrolling = engaged reading
- **Hesitation patterns:** Long pauses before tapping answer = thinking (good) vs distraction (check RT)

**Acceptance:**
- [ ] `TouchPatternService` wraps a `GestureDetector` or `Listener` at the session screen level
- [ ] Collects: `tapIntervalMs`, `touchAreaPx`, `swipeVelocity` in rolling 20-event window
- [ ] Touch pattern score: coefficient of variation (CV) of tap intervals — CV < 0.3 = consistent = 1.0, CV > 0.8 = erratic = 0.2
- [ ] Does NOT record tap coordinates (privacy — no keylogging)
- [ ] Only timing and pressure/area metadata

**Test:**
```dart
test('consistent tap rhythm scores high', () {
  final intervals = [1200, 1180, 1220, 1190, 1210]; // ~1.2s each, low variance
  expect(TouchPatternAnalyzer.score(intervals), greaterThan(0.8));
});

test('erratic tap rhythm scores low', () {
  final intervals = [500, 3000, 800, 5000, 200]; // highly variable
  expect(TouchPatternAnalyzer.score(intervals), lessThan(0.4));
});
```

### FOC-002.4: Environment Signal
**Files:**
- `lib/services/sensors/environment_sensor_service.dart` — NEW
- `pubspec.yaml` — add `light: ^3.0.2`, `proximity_sensor_plugin: ^2.0.1`, `battery_plus: ^6.0.3`

**What it detects:**
- **Ambient light stability:** Constant light = stable study environment; fluctuating = moving around
- **Proximity sensor:** Phone face-down = break/distraction; near-face = possible call
- **Battery level:** <15% + late evening = likely exhausted student
- **Light level categories:** Dark (<50 lux), dim (50-200), normal (200-500), bright (>500)

**Acceptance:**
- [ ] `EnvironmentSensorService` aggregates light, proximity, and battery into one score
- [ ] Light stability: variance of lux readings over 30s window. Low variance = stable = good
- [ ] Proximity: face-down events tracked. >2 face-down events in 5 min = environment instability
- [ ] Battery context: `isCriticalBattery` (< 15%), `isLateNightLowBattery` (< 30% after 22:00)
- [ ] Environment score: weighted average of light stability (0.4), proximity stability (0.3), battery context (0.3)
- [ ] iOS fallback: ambient light not available via public API → use screen brightness as proxy
- [ ] Android: `TYPE_LIGHT` sensor via `light` package

**Test:**
```dart
test('stable indoor lighting scores high', () {
  final luxReadings = [320, 315, 325, 318, 322]; // stable ~320 lux
  expect(EnvironmentAnalyzer.lightStabilityScore(luxReadings), greaterThan(0.8));
});

test('fluctuating light scores low', () {
  final luxReadings = [320, 50, 800, 120, 500]; // wildly varying
  expect(EnvironmentAnalyzer.lightStabilityScore(luxReadings), lessThan(0.4));
});
```

### FOC-002.5: Sensor Aggregator
**Files:**
- `lib/services/sensors/sensor_aggregator.dart` — NEW

**Acceptance:**
- [ ] `SensorAggregator` combines all 4 sensor services into a single `SensorSnapshot`
- [ ] `SensorSnapshot`: `motionScore?`, `appFocusScore?`, `touchPatternScore?`, `environmentScore?`, `timestamp`, `availableSensorCount`
- [ ] Streams `SensorSnapshot` at configurable interval (default: every question OR every 30s, whichever is first)
- [ ] Handles sensor permission denials gracefully (score = null for denied sensors)
- [ ] Includes `SensorHealth` diagnostic: which sensors are active, denied, unavailable

## Research References
- ADHD study: smartwatch tools achieved 88.9% emotion recognition, 40% quiz improvement
- Esterman (2013): RT variability validated against neural markers
- Focus Degradation Research doc, Sections 2.5, 2.12, 4.4
