# Interactive Projection Board — Research & Kimi Agent Super Prompt

## Context: Cena Platform

Cena is a personal AI mentor system for students. It features a visible knowledge graph, adaptive methodology switching, cognitive load personalization, and curriculum-agnostic architecture. Current platforms: web app + mobile app (mobile-first). This document explores extending Cena into the **physical world** via interactive projection boards.

---

## The Vision

Project Cena's learning content (simulations, games, quizzes, knowledge graph visualizations, challenge scenarios) onto a **physical surface** (wall, table, floor) and allow users to **touch and interact with it physically** — with real-time feedback to the Cena mobile/web app.

### Use Cases for Cena
1. **Projected Knowledge Graph** — students physically touch, drag, and explore their knowledge graph on a wall/table
2. **Interactive Quizzes** — project questions onto a surface; students tap answers physically
3. **STEM Simulations** — physics simulations (gravity, circuits, chemistry) projected and manipulated by hand
4. **Collaborative Learning** — multiple students interact with the same projected surface simultaneously
5. **Gamified Challenges** — project challenge-based scenarios that students solve with physical gestures
6. **Classroom Deployment** — teachers project Cena content for group instruction with student participation

---

## Feasibility: YES — This is a Valid, Proven Domain

This is a well-established technology. Commercial products operate in **15,000+ locations worldwide** (EyeClick alone serves McDonald's, Marriott, NASA). The tech stack is mature:

- **DIY builds**: $500–$1,500 with open-source software
- **Professional installations**: $4,000–$28,000+
- Multiple open-source frameworks exist at various maturity levels
- Depth cameras are affordable ($100–$400)

---

## Hardware Requirements

### Projectors

| Type | Throw Ratio | Distance for 100" | Best For | Cost |
|------|-------------|-------------------|----------|------|
| **Ultra-Short Throw (UST)** | < 0.4 | 10–20 inches | Wall (eliminates shadows) | $800–$3,000+ |
| **Short Throw** | 0.4–1.0 | 3–8 feet | Walls and some floors | $500–$1,500 |
| **Long Throw** | > 1.0 | 8–10+ feet | Not recommended for interactive | $200–$800 |

Key specs: **3,000+ lumens** minimum, **1080p**, 16:9/16:10 aspect ratio. UST preferred for walls (no shadows); short-throw ceiling-mounted for floors.

### Depth Cameras / Sensors

| Camera | Type | Depth Res | FOV | Range | Price | Notes |
|--------|------|-----------|-----|-------|-------|-------|
| **Orbbec Femto Bolt** | ToF | 1024x1024 | 120° WFOV | 0.25–5.5m | ~$300–400 | Official Azure Kinect successor. Best current option. |
| **Orbbec Femto Mega** | ToF + Edge AI | 1MP depth, 4K RGB | Wide | Similar | ~$500+ | Built-in NVIDIA Jetson Nano for on-device AI. |
| **Intel RealSense D455** | Active IR Stereo | 1280x720 | 87×58° | 0.4–6m+ | ~$300 | Global shutter, built-in IMU, best for longer range. |
| **Intel RealSense D435** | Active IR Stereo | 1280x720 | 85×58° | 0.3–3m | ~$180–250 | Good for prototyping. |
| **Orbbec Astra** | Structured Light | 640x480 | 60° | 0.6–8m | ~$100–150 | Budget option. |

### Touch Detection Principle
Depth camera captures reference surface. When a fingertip comes within ~30mm of the surface → "press" event. Movement while in contact → swipe/drag. Distance exceeds threshold → "release" event.

### Computing
- **Minimum**: Modern PC, USB 3.0, dedicated GPU
- **Recommended**: Intel i5/i7 or Ryzen 5/7, 16GB RAM, NVIDIA GPU (CUDA)
- **Budget**: Raspberry Pi 4/5 for basic setups
- **Edge AI**: Orbbec Femto Mega (built-in Jetson Nano)

---

## Existing Open-Source Projects

| Project | Stack | What It Does |
|---------|-------|-------------|
| **[ProGAF](https://github.com/jcfandinocal/progaf)** | Python (Pygame, Panda3D, OpenCV, MediaPipe) | Full framework for games with real-world object tracking + projection mapping. `pip install progaf`. Detects faces, eyes, hands, shapes, blobs. |
| **[Kinect Ripple](https://github.com/microsoft/kinect-ripple)** | C# | Microsoft's dual projection infotainment system. Auto-calibration, SDK, content editor. |
| **[openFloor](https://github.com/vcazan/openFloor)** | Processing/Flash | Detects x,y coordinates of moving blobs for interactive floor projection. |
| **[Touch-Projectors](https://github.com/huzz/Touch-Projectors)** | Python | Converts any surface into interactive touchpad via 4-point selection. |
| **[iPlanes](https://github.com/kushalchaudhari21/iPlanes-InteractivePlanes)** | Python (OpenCV) | Converts projectors into interactive interfaces — no sensors needed (< $1 hardware). |
| **[InteractiveProjectionLib](https://github.com/alvesmaicon/InteractiveProjectionLib)** | OpenCV | Interactive projection library. |
| **[PiFloor](https://github.com/shakram02/PiFloor)** | Android | Portable interactive floor for child education. |
| **[CVDepthProjectorToolkit](https://github.com/aaronsherwood/CVDepthProjectorToolkit)** | C++ (Cinder) | Tracks people with Orbbec Astra, projects on ground/wall, OpenCV homography. |
| **[ofxReprojection](https://github.com/Illd/ofxReprojection)** | C++ (openFrameworks) | Dynamic projection mapping with depth camera, automated chessboard calibration. |
| **[ofxPiMapper](https://ofxpimapper.com/)** | C++ (openFrameworks) | Projection mapping on Raspberry Pi. |
| **[procam-calibration](https://github.com/kamino410/procam-calibration)** | Python (OpenCV) | Projector-camera calibration using chessboard + gray codes. Subpixel accuracy. |
| **[AR-Projection-Mapping](https://github.com/ddelago/AR-Projection-Mapping)** | Python (OpenCV) | Projector-camera calibration using ArUco markers. |
| **[UnityProjectionMapping](https://github.com/andrewmacquarrie/UnityProjectionMapping)** | C# (Unity + OpenCVSharp) | Projection mapping in Unity with 7-point calibration. |
| **[KlakSpout](https://github.com/keijiro/KlakSpout)** | C# (Unity) | GPU texture sharing Unity ↔ TouchDesigner/Resolume. |
| **[MapMap](https://mapmapteam.github.io/)** | C++ | Free projection mapping software. |

---

## Commercial Solutions

| Product | Type | Key Features | Pricing |
|---------|------|-------------|---------|
| **[EyeClick](https://eyeclick.com/) (Beam/Obie)** | Turnkey HW+SW | 350+ games, all-in-one unit, 15,000+ locations | ~$6/day subscription |
| **[Lumo Play](https://www.lumoplay.com/)** | Software-only | Works with any PC/projector/sensor, 12+ game types | ~$50–100/month |
| **[Po-motion](https://po-motion.com/)** | Software-only | Works with PC + display + webcam/Kinect, low entry cost | Tiered |
| **[TouchDesigner](https://derivative.ca/)** | Visual programming | Node-based real-time, built-in projection mapping, supports all sensors | Free (non-commercial) |
| **[MotionMagix](https://motionmagix.com/)** | HW+SW | Floors, walls, tables | Varies |

---

## Software / Tech Stack Options

### Computer Vision

| Library | Language | Use Case |
|---------|----------|----------|
| **OpenCV** | C++/Python/JS | Core: blob detection, homography, calibration, perspective transforms |
| **MediaPipe** | Python/JS/C++ | Hand tracking (21 keypoints), gesture recognition, pose estimation. Runs in browser. |
| **OpenNI** | C++/Python | Middleware for depth cameras (Kinect, Xtion, Orbbec) |

### Game/Rendering Engines

| Engine | Projection Support | Notes |
|--------|-------------------|-------|
| **Unity** | Mature — UnityProjectionMapping, KlakSpout | Best tooling. C#. Spout/NDI output to TouchDesigner. |
| **Godot** | Custom setup via Projection matrix | Free/open-source. Growing community. GDScript/C#. |
| **Unreal Engine** | NDI + Spout plugins for UE5 | Most powerful rendering, heaviest. |
| **TouchDesigner** | Native (Stoner, Kantan Mapper, CamSchnappr) | Node-based real-time. Python + GLSL. Supports Kinect, RealSense, Leap Motion. |

### Calibration

| Tool | Method |
|------|--------|
| **procam-calibration** | Chessboard + gray codes, local homographies, OpenCV stereoCalibrate |
| **OpenCV built-in** | `stereoCalibrate`, `initProjectorRectifyMap`, ArUco markers |
| **ofxReprojection** | Projected chessboard auto-detected by depth camera |
| **TouchDesigner CamSchnappr** | Built-in projector-camera calibration |

### Video Pipeline

Typical flow: **Unity/Godot** (content) → **Spout/Syphon** (local GPU sharing) or **NDI** (network) → **TouchDesigner/MadMapper** (warping, mapping) → **Projector output**

---

## Mobile/Web App Feedback Loop Architecture

```
┌─────────────────────────────────────────────────────────┐
│                  PHYSICAL SPACE                          │
│                                                         │
│  [Projection Surface] ◄──projects── [Projector]         │
│        │                              ▲                  │
│        ▼                              │ video            │
│  [Depth Camera/Sensor]          [Rendering PC]           │
│        │                         ▲         │             │
│        │ depth frames            │         │             │
│        ▼                         │         │             │
│  [CV Processing] ──touch events──┘         │             │
│                                            │             │
└────────────────────────────────────────────┼─────────────┘
                                             │
                              WebSocket / MQTT│
                                             │
                    ┌────────────────────────┼──────────┐
                    │        NETWORK         │          │
                    │                        ▼          │
                    │              [WebSocket Server]    │
                    │               ▲            │      │
                    │               │            ▼      │
                    │        [Cena Mobile]  [Cena Web]  │
                    └───────────────────────────────────┘
```

### Communication Options

| Method | Latency | Best For |
|--------|---------|----------|
| **WebSocket** (recommended) | Low (~ms) | Real-time bidirectional. CV publishes touch events; mobile/web subscribe. |
| **MQTT** | Low | Multi-client pub/sub. Scales well for classrooms. |
| **REST + SSE** | Medium | Simpler but higher latency. Commands via REST, updates via SSE. |

### Mobile App Integration Points
- **Real-time score/status**: Show game scores, quiz progress, leaderboards
- **Control interface**: Select games, adjust settings, trigger events
- **Personal feedback**: Vibration/sound on phone when user interacts with projected surface
- **Multi-user ID**: QR codes or NFC to identify which player is which on the surface
- **Knowledge graph sync**: Physical interactions with projected graph update the student's Cena profile

---

## Key Challenges

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| **Ambient lighting** | High | Control environment; use 3,000+ lumen projectors; avoid direct sunlight |
| **Shadow interference** | High | UST projectors for walls; ceiling-mount for floors |
| **Calibration complexity** | Medium | Use procam-calibration or ArUco markers; auto-calibration in some systems |
| **Sensor occlusion** | Medium | Overhead mounting; multiple sensors for large areas |
| **Latency** | Medium | Short processing pipeline; GPU acceleration (CUDA); 30fps depth is sufficient |
| **Surface quality** | Low–Med | Matte white/light grey; projection paint (Screen Goo); avoid glossy surfaces |
| **Content development** | Medium | ProGAF/Lumo Play templates for prototyping; Unity/Godot for custom content |

---

## Recommended Starting Architecture for Cena

### Phase 1: Prototype (Budget ~$600–$1,500)
- **Projector**: Short-throw, 3,000+ lumens, 1080p (~$300–500 used)
- **Depth camera**: Orbbec Astra ($100–150) or RealSense D435 ($180–250)
- **PC**: Any modern PC with NVIDIA GPU (or use existing)
- **Software**: Python + OpenCV + MediaPipe → WebSocket → Cena web app
- **Calibration**: OpenCV ArUco markers or procam-calibration
- **Content**: Web-based (HTML Canvas / WebGL) projected via fullscreen browser

### Phase 2: Production
- **Projector**: UST, 4,000+ lumens (Epson/BenQ)
- **Sensor**: Orbbec Femto Bolt ($300–400)
- **Software**: Unity (content) → Spout → TouchDesigner (mapping) → Projector
- **Mobile**: WebSocket server (Node.js socket.io) for real-time Cena app sync
- **Calibration**: TouchDesigner CamSchnappr or automated chessboard

### Phase 3: Classroom Product
- Turnkey hardware kit (projector + sensor + mini-PC in single enclosure)
- Teacher dashboard in Cena web app to control projected content
- Student phones as personal response devices
- Multi-user tracking (up to 6–10 simultaneous touch points)

---

## Kimi Agent Super Prompt

> **Copy everything below this line and paste it as your Kimi prompt:**

---

### PROMPT START

You are an expert systems architect specializing in interactive projection systems, computer vision, and EdTech product development. I need your help designing and planning an **Interactive Projection Board** feature for **Cena** — a personal AI mentor platform for students.

#### Background — What Cena Is

Cena is an adaptive learning platform with:
- **Student-facing knowledge graph** — interactive visual map of what the student knows, with mastery levels, relationships between concepts, and temporal tracking
- **Adaptive methodology switching** — automatically switches teaching methods (Socratic, spaced repetition, project-based, Feynman technique, challenge-based) when stagnation is detected
- **Cognitive load personalization** — per-student fatigue detection, 5–10 min learning units, 20–25 min session caps
- **Gamification** — stealth gamification, streaks, XP, skill proficiency meters, knowledge graph growth as primary reward
- **Platforms** — web app + mobile app (mobile-first), curriculum-agnostic (starting with Israeli Bagrut exams)
- **Visual style** — flat illustrations + formula cards + icon grids for learning content; dark/glowing network nodes for knowledge graph; skill proficiency meters with percentages per domain
- **Dynamic diagram generation** — concept cards, process flow diagrams, and icon-based topic grids generated on-the-fly per concept (not static assets)

#### What I Want to Build

An **Interactive Projection Board** that:
1. **Projects Cena content** (knowledge graph, quizzes, simulations, challenges, concept diagrams) onto a physical surface (wall, table, or floor)
2. **Detects physical touch/gestures** using depth cameras or IR sensors — users interact by touching, swiping, tapping the projected content
3. **Feeds interaction data back** to the Cena mobile/web app in real-time via WebSocket/MQTT
4. **Supports multiple users** simultaneously (classroom setting with 2–10 students)
5. **Syncs with student profiles** — physical interactions update the student's knowledge graph, progress, and mastery levels in their Cena account

#### Specific Questions I Need Answered

1. **Architecture Design**: Design a complete system architecture for this feature. Include:
   - Hardware components and their connections
   - Software stack (CV processing, rendering, communication, calibration)
   - Data flow from physical touch → CV processing → Cena backend → mobile app
   - How to handle multi-user identification on the projected surface

2. **Tech Stack Recommendation**: Given that Cena is a web/mobile platform, recommend the optimal tech stack. Consider:
   - Should content rendering be web-based (HTML Canvas/WebGL projected via browser) or engine-based (Unity/Godot)?
   - Which depth camera provides the best price/performance for a classroom setting?
   - What's the best calibration approach for a product that non-technical teachers need to set up?
   - How to minimize latency in the touch → visual feedback loop?

3. **Integration with Cena**: How should the projection board integrate with Cena's existing systems?
   - Knowledge graph interactions (drag nodes, explore connections, tap to drill into concepts)
   - Quiz/challenge interactions (tap answers, draw solutions, gesture-based input)
   - Score/progress sync to student profiles
   - Teacher control panel (select what to project, manage student sessions)

4. **Content Adaptation**: Cena already generates dynamic diagrams and concept cards. How should these be adapted for projection?
   - Resolution and sizing considerations
   - Touch target sizes (fingers are larger than mouse pointers)
   - Color adjustments for projected light (vs screen-emitted light)
   - Multi-user layout strategies

5. **MVP Plan**: Design a minimum viable prototype I can build in 2–4 weeks with:
   - Budget under $1,000
   - One projector + one depth camera + existing PC
   - A single interactive experience (e.g., projected knowledge graph exploration)
   - WebSocket feedback to a Cena web app running on a phone

6. **Classroom Deployment Plan**: How to scale this into a classroom product:
   - Turnkey hardware packaging
   - Setup/calibration UX for teachers
   - Multi-student identification and tracking
   - Content management and scheduling

7. **Known Open Source Resources**: Evaluate these projects for our use case and recommend which to build upon:
   - ProGAF (Python, Pygame/Panda3D/OpenCV/MediaPipe)
   - Kinect Ripple (C#, Microsoft)
   - CVDepthProjectorToolkit (C++, Cinder, Orbbec)
   - procam-calibration (Python, OpenCV)
   - UnityProjectionMapping (C#, Unity + OpenCVSharp)
   - TouchDesigner (node-based visual programming)
   - ofxReprojection (C++, openFrameworks)

8. **Risk Assessment**: What are the biggest risks and how to mitigate them? Consider:
   - Ambient lighting in classrooms
   - Calibration drift over time
   - Latency requirements for responsive interaction
   - Hardware failure modes
   - Cost per classroom

Please provide a **detailed, actionable response** with specific technology choices, architecture diagrams (ASCII is fine), code snippets where relevant, and a phased implementation plan. Prioritize practicality — I want to build a working prototype, not a theoretical paper.

### PROMPT END

---

## Sources

- [ProGAF](https://github.com/jcfandinocal/progaf) — Python game framework with projection mapping
- [Kinect Ripple](https://github.com/microsoft/kinect-ripple) — Microsoft interactive floor/screen
- [CVDepthProjectorToolkit](https://github.com/aaronsherwood/CVDepthProjectorToolkit) — Depth camera + projector toolkit
- [procam-calibration](https://github.com/kamino410/procam-calibration) — Projector-camera calibration
- [UnityProjectionMapping](https://github.com/andrewmacquarrie/UnityProjectionMapping) — Unity projection mapping
- [KlakSpout](https://github.com/keijiro/KlakSpout) — Unity GPU texture sharing
- [ofxReprojection](https://github.com/Illd/ofxReprojection) — openFrameworks dynamic projection
- [ofxPiMapper](https://ofxpimapper.com/) — Raspberry Pi projection mapping
- [MapMap](https://mapmapteam.github.io/) — Open source projection mapping
- [Touch-Projectors](https://github.com/huzz/Touch-Projectors) — Surface-to-touchpad conversion
- [iPlanes](https://github.com/kushalchaudhari21/iPlanes-InteractivePlanes) — Sub-$1 interactive projection
- [EyeClick](https://eyeclick.com/) — Commercial interactive projection (350+ games, 15,000+ locations)
- [Lumo Play](https://www.lumoplay.com/) — Software-only interactive projection
- [TouchDesigner](https://derivative.ca/) — Professional visual programming for projection
- [MediaPipe](https://developers.google.com/mediapipe) — Google's hand/gesture tracking (runs in browser)
- [Orbbec Femto Bolt](https://www.orbbec.com/products/tof-camera/femto-bolt/) — Azure Kinect successor
- [Intel RealSense D455](https://www.realsenseai.com/products/real-sense-depth-camera-d455f/) — Depth camera
