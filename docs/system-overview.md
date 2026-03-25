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

## Open Questions

- How does the student interact with the system? (chat, dashboard, scheduled sessions)
- What defines "high-grade" — university level, advanced high school, gifted programs?
