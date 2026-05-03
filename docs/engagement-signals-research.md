# Engagement Detection Research — Camera, Behavioral, and Biometric Signals

> **Date:** 2026-03-26
> **Status:** Research complete
> **Conclusion:** Behavioral signals first, camera-based head pose v1.5, emotion inference only for non-EU markets with heavy consent

---

## Critical Regulatory Finding

### EU AI Act (in force February 2, 2025)
**Article 5(1)(f) explicitly PROHIBITS using AI systems to infer emotions in educational institutions.** Penalty: up to 7% of global annual turnover. This covers:
- Facial emotion recognition in schools/learning platforms
- Inferring student interest/attention from camera data
- Any emotion analytics applied to students

**If Cena ever serves EU students, camera-based emotion inference is illegal.** This applies regardless of where the company is incorporated.

### Israel (Amendment 13, effective August 2025)
- Biometric data now classified as "highly sensitive information"
- EU adequacy status reconfirmed January 2025 — Israel aligns with GDPR
- Parental/guardian consent required for biometric data from students under 18

### COPPA (USA, updated April 2024)
- Biometric identifiers (facial patterns, voiceprints) explicitly added to personal information definition
- EdTech platforms: separate, explicit parental consent required for AI features processing children's input

---

## Signal Production-Readiness Matrix

| Signal | Privacy Risk | Complexity | Real-World Accuracy | Legal Risk | Recommended Phase |
|--------|-------------|------------|--------------------|-----------|--------------------|
| Response timing | None | Low | High (established) | None | **V1 — immediately** |
| Keystroke dynamics (deletion rate, hesitation) | Low | Low | 89% (negative vs positive state) | None | **V1** |
| Scroll/interaction patterns | None | Low | Moderate | None | **V1** |
| Time-of-day circadian patterns | None | Low | High (9-34% performance variation) | None | **V1** |
| Ambient noise level | Low | Low | Moderate | None | **V1** |
| Answer change patterns (changing before submit) | None | Low | High | None | **V1** |
| Head pose / looking away detection | Medium | Medium | High (reliable) | Low (not emotion inference) | **V1.5 with consent** |
| Blink rate estimation | Medium | Medium | Moderate | Medium | V1.5 |
| Voice tone analysis (oral answers) | Medium | Medium | 84-85% multimodal | Medium | V2 |
| Emotion classification from face | **High** | High | **55-65% real-world** | **ILLEGAL in EU** | Post-V2, non-EU only |
| Gaze tracking (non-TrueDepth) | High | High | Low-moderate (100-200px error) | Medium | Research only |
| Cognitive load from camera | High | Very High | Unreliable without EEG | High | Not viable |

---

## Recommended Architecture: Tiered Engagement Detection

### Tier 1 — Behavioral Signals (V1, zero legal risk)
Already partially in Cena's design. Expand to include:

- **Response timing analysis**: Time-to-first-keypress (retrieval fluency), total response time, "fast wrong" vs "slow wrong" distinction
- **Deletion/backspace patterns**: High backspace rate = uncertainty/confusion
- **Answer change frequency**: Changing answers before submit = anxiety
- **Typing speed variability**: High variance within session = cognitive overload
- **Scroll behavior**: Rapid scrolling past explanations = disengagement; re-reading = confusion
- **Session engagement curve**: Time between interactions within a session (slowing = fatigue)
- **Time-of-day performance profiling**: Build circadian rhythm model per student, schedule harder content at peak times
- **Ambient noise**: Microphone level monitoring (no speech content), adjust hint thresholds

These signals feed directly into the StudentActor's stagnation detection and cognitive load profiling.

### Tier 2 — Camera-Based Attention (V1.5, with explicit consent)

- **Head pose / looking away**: MediaPipe Face Mesh or ML Kit via react-native-vision-camera. Detect when student is off-screen (yaw > 30-40°)
- **Blink rate**: ML Kit eye open probability. Reduced blink rate correlates with high cognitive load
- **On-device only**: No video frames transmitted to server. Process locally, transmit only derived engagement score
- **Battery optimization**: Sample every 5-10 seconds, not every frame. Use NPU offloading (Apple Neural Engine, Qualcomm Hexagon)
- **Explicit opt-in**: Camera engagement is separate from core learning. Students can use Cena fully without enabling it

**NOT emotion inference** — this is attention/presence detection, which is not prohibited by the EU AI Act.

### Tier 3 — Emotion Inference (Post-V2, non-EU markets only)

- Custom emotion classifier fine-tuned on student population (6-12 month R&D)
- Multimodal: face + behavioral signals + voice tone
- Real-world accuracy target: 70-78% (multimodal)
- Requires: parental consent, on-device processing, ethics review, IRB approval
- **Never deploy in EU markets** — prohibited by AI Act Article 5(1)(f)

---

## Technical Implementation (React Native)

### V1 — Behavioral (no camera)
```
Existing interaction events in StudentActor
  → Keystroke timing interceptor (middleware on text inputs)
  → Scroll position tracking (onScroll handlers)
  → Answer change event logging
  → Ambient noise: Audio.getLevel() periodic sampling
  → All signals feed into StagnationDetectorActor sliding window
```

### V1.5 — Camera-Based Attention
```
react-native-vision-camera (v4+)
  → Frame processor (every 5-10 seconds)
  → react-native-vision-camera-mlkit (face detection + landmarks)
     OR react-native-fast-tflite (custom lightweight model)
  → Derive: head_pose_yaw, eyes_open, face_present
  → Engagement score = f(behavioral_signals + attention_signals)
  → Feed to StudentActor via SignalR
  → No video data leaves device
```

Battery impact: Sampling every 5-10 seconds with NPU offloading = ~5-10% additional battery drain per study session. Acceptable.

---

## Key Insight

> The signals most useful for adaptive learning (emotion, cognitive load) have the **worst real-world accuracy (55-65%)**, the **highest regulatory risk (EU AI Act prohibition)**, and the **most privacy exposure**. The signals that are privacy-safe and technically trivial (response timing, keystroke patterns, scroll behavior) are **already in Cena's design** and provide 60-70% of the adaptive signal value.

**Build the behavioral signal layer deep and well. Add camera as an enhancement, not a dependency.**

---

## Sources
- EU AI Act Article 5(1)(f) — Prohibited AI Practices
- FER2013 benchmark: EmoNeXt state-of-art 76-78%
- AffectNet: real-world 60-67% (8-class)
- DAiSEE (IIT Hyderabad): e-learning engagement benchmark
- JMIR 2024: Smartphone keyboard backspace rates predict mood states
- Nature npj Digital Medicine 2024: Keyboard dynamics predict affect
- MDPI Sensors 2026: ARKit TrueDepth real-time emotion recognition
- Israel Amendment 13 (August 2025): Biometric data = highly sensitive
- COPPA 2024 amendments: Biometric identifiers added
- Neuroscience News 2025: Circadian rhythm synaptic plasticity peaks
