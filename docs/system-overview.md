# Cena - Personal Student Mentor System

## Overview

Cena is a personal mentor system designed for high-grade students. It serves as an intelligent companion that remembers, organizes, and builds upon each student's learning journey.

## Core Capabilities

### Memory & Knowledge Tracking
- Remembers learned concepts, ideas, thoughts, and skills
- Organizes knowledge into structured, interconnected maps
- Tracks progression and mastery levels per topic
- Identifies knowledge gaps and areas for reinforcement

### Personalized Mentoring
- Adapts to each student's learning style and pace
- Applies specific methodologies to enhance skills and knowledge
- Provides targeted exercises and challenges
- Offers contextual guidance based on the student's history

### Skill Enhancement
- Monitors skill development over time
- Suggests next steps based on current proficiency
- Connects new material to previously learned concepts
- Reinforces weak areas through spaced repetition and practice

## Target Users

- High-grade students seeking structured personal mentorship
- Students who want to organize and retain their learning effectively

## Methodology Approach

### Adaptive Methodology Selection
- The system selects the best methodology per student automatically
- Supported methods include: Socratic method, spaced repetition, project-based learning, Bloom's taxonomy progression, Feynman technique, and others
- The system profiles each student to determine which method is most effective for them
- Tracks per-student method effectiveness over time

### Stagnation Detection & Switching
- Monitors progress and detects when a student plateaus
- Automatically switches to a different methodology when stagnation is detected
- The switch happens **seamlessly** — the student is not made aware of the methodology change
- Stagnation detection criteria (time-based, attempt-based, etc.) — TBD

### Student Control
- Students can request a different learning approach at any time (e.g., "I'd rather learn this through a project")
- The system honors the request without exposing internal methodology labels

## Knowledge Storage

### Knowledge Graph
- Student knowledge is stored as a **knowledge graph**
- Concepts are nodes, relationships are edges (prerequisite, builds-on, related-to)
- Each node tracks: mastery level, date learned, review history, method effectiveness
- The graph powers gap detection, learning path suggestions, and concept connections

### Visualization
- Students see an interactive visual map of their knowledge graph
- Node colors/sizes reflect mastery level (mastered, in progress, weak)
- Edges show how concepts relate to each other
- Clusters group related topics naturally
- Graph grows visibly over time as the student learns — serves as motivation

## Subjects & Scope

- **Initial focus: STEM** (Science, Technology, Engineering, Mathematics)
- Syllabus-based — aligned to high-grade curriculum standards
- **Depth levels** — each topic has multiple levels of depth, allowing students to go from surface understanding to deep mastery
- Syllabus will be provided to the system as structured input

## Interaction & Platform

### Multi-Level Interaction
- Interaction is not a single mode — the system dynamically presents what's relevant
- **Interactive sessions** — guided mentoring conversations
- **Concept visualization** — knowledge graph, concept maps, relationships shown in context
- **Thought tracking** — surfaces the student's own recorded thoughts and ideas
- **Data presentation** — progress metrics, mastery levels, learning patterns
- The UI is a **living workspace** that adapts based on what the student is doing

### Student Annotations
- Students can annotate concepts — add their own thoughts, notes, and ideas
- Annotations are captured by the system as input to understand the student's thinking
- The knowledge graph is updated based on annotations (new connections, thoughts, insights)

### Platforms
- **Web app** and **mobile app** (mobile-first approach)
- Consistent experience across both platforms

## Target Audience

### Syllabus-Agnostic Design
- The system is designed to support **any country's syllabus** as structured input
- Syllabus defines the curriculum tree, subjects, and depth levels for that country
- The architecture does not hardcode any specific curriculum

### Initial Target: Bagrut (Israeli Matriculation)
- First deployment targets students preparing for **Bagrut** exams
- Syllabus follows the Israeli Ministry of Education curriculum
- STEM subjects: Mathematics, Physics, Chemistry, Biology, Computer Science
- Depth levels align with the Bagrut study units system (3/4/5 units per subject)

## Visual Design

### Graphic Style
- **Flat illustration style** for concept diagrams — clean, clear layouts with arrows and graphic elements showing processes and relationships (similar to FigureLabs scientific figures)
- **Colorful icon cards** for topic navigation — each concept/topic gets a distinct colored card with a simple illustrative icon and label (similar to smartyme_physics grid style)
- **Tech-inspired visuals** for the knowledge graph and brand — glowing network nodes, interconnected data points, dark backgrounds with vibrant highlights (similar to Technion/HUJI AI course visuals)
- White/clean backgrounds for learning content; dark/immersive backgrounds for the knowledge graph visualization

### Dynamic Diagram Generation
- The system must be capable of **generating or serving concept diagrams on-the-fly** for any topic in the syllabus
- **Concept cards** — labeled illustrations paired with core formulas/equations (e.g., aerodynamics wing diagram + L = 1/2pv^2SC_L)
- **Process flow diagrams** — step-by-step visual flows with arrows showing how things work (chemical reactions, biological processes, circuit flows)
- **Icon-based topic grids** — color-coded navigational cards per topic area, each with a distinct icon
- Diagrams are not static assets — they are generated/composed per concept so the system can scale across any syllabus without manually creating thousands of images
- Potential approaches: AI-generated SVGs, templated illustration engine, or a hybrid with a curated asset library + dynamic composition

### Design Principles
- Visuals should make complex concepts feel approachable and clear
- Every diagram, figure, and concept should have a consistent illustrative style
- The overall aesthetic should feel modern, tech-forward, and engaging for students

## Gamification

- Learning progress is gamified to keep students motivated and engaged
- Elements include: XP/points for completing concepts, streaks for daily engagement, badges/achievements for milestones
- Leveling system tied to mastery depth — unlocking deeper levels feels like progression
- Leaderboards (optional) for friendly competition
- The knowledge graph itself serves as a visual reward — watching it grow is inherently motivating

## Cognitive Load Management

### Quantum Learning (Small Units of Information)
- Content is delivered in **small, digestible quants** — bite-sized pieces that prevent cognitive overload
- Each learning session is calibrated to avoid exhausting the student
- The system learns each student's **personal thresholds** — how much new information they can absorb before fatigue

### Threshold Detection
- Monitors engagement signals (response time, accuracy drop-off, session duration patterns)
- Builds a per-student cognitive load profile over time
- Adjusts session length and content density dynamically based on the student's current state

### Estimated Timelines
- The system provides **personalized time estimates** for achieving specific goals (e.g., "Master 5-unit Math by Bagrut exam date")
- Timelines factor in: current knowledge level, learning pace, available study time, and historical performance
- Estimates update dynamically as the student progresses
