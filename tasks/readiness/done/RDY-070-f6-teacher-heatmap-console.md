# RDY-070 — F6: Teacher classroom heatmap console (zero-setup homework)

- **Wave**: B
- **Priority**: HIGH (gates `School` institute adoption)
- **Effort**: 3-4 engineer-weeks
- **Dependencies**: classroom-consumer split (shipped); InstructorLed classroom model (shipped)
- **Source**: [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F6; [synthesis](../../docs/research/cena-user-personas-and-features-2026-04-17.md) F6

## Problem

Ofir-type teachers are the gatekeeper for `School` institute adoption. Hebrew incumbents (Geva, GOOL) have no teacher console. Ofir has 0 prep minutes; any feature asking him to author or tag content is dead on arrival. What he needs: 2-minute daily glance, 15-second scannability, zero-setup homework assignment.

## Scope

**Console layout:**
- Topic × student heatmap (rows = Ministry syllabus topics, cols = students)
- Cell = mastery with confidence (opacity encodes sample size per Dr. Yael)
- Two buttons: "Assign 15 min to whole class on topic X" + "Assign to selected students"
- Monday-morning summary: "last week your class worked X hours, topic gains, 3 kids stuck on Y"

**Technical:**
- New Marten projection `ClassMasteryHeatmapProjection`, per-classroom read model
- Idempotent, rebuild-safe (Dina's ask)
- Endpoint: `GET /api/v1/institutes/{instituteId}/classrooms/{classroomId}/mastery-heatmap`
- Scoping: teacher auth + InstructorLed + School institute

**Topic grouping must match Ministry syllabus hierarchy** (Prof. Amjad demand) — not internal taxonomy.

## Files to Create / Modify

- `src/shared/Cena.Domain/Projections/ClassMasteryHeatmapProjection.cs`
- `src/api/Cena.Admin.Api/Features/TeacherConsole/HeatmapEndpoint.cs`
- `src/admin/full-version/src/views/teacher/ClassHeatmap.vue`
- `src/admin/full-version/src/views/teacher/AssignHomework.vue`
- `src/shared/Cena.Domain/Syllabus/MinistryTopicHierarchy.cs` — locked topic tree
- `docs/design/teacher-heatmap-design.md`

## Acceptance Criteria

- [ ] Heatmap page loads < 1s for classroom of 32 (Ofir's ask)
- [ ] Cell color = mastery; cell opacity/badge = sample-size confidence (Dr. Yael requirement)
- [ ] "Assign 15 min" button creates homework with 0 authoring (Cena picks each student's top weakness within teacher-selected topic)
- [ ] Monday-morning digest goes to teacher email 07:00
- [ ] Scoping enforced: teacher can only see own classrooms
- [ ] Projection rebuildable from event store without data loss (Dina's ask)

## Success Metrics

- **Teacher weekly active rate**: target ≥ 70% of onboarded teachers
- **Time-to-first-assignment**: target < 2 minutes from console entry
- **Class-level mastery gain per week of assignments**: target measurable uplift vs control classrooms
- **Teacher NPS**: target ≥ 40

## ADR Alignment

- Classroom-consumer split (InstructorLed + School) per [classroom-consumer-split.md](../../docs/compliance/classroom-consumer-split.md)
- FERPA school-official exception: teacher viewing own classroom's student mastery is legitimate instructional use
- ADR-0003: heatmap shows mastery + sample size only; no misconception codes

## Out of Scope

- Cross-classroom/principal view (separate higher-role task)
- Teacher-authored content (out of scope by design — zero authoring)
- PrivateTutor or CramSchool views (different consent model)

## Assignee

Unassigned; Dina for projection, Dr. Yael for confidence encoding, Prof. Amjad for topic hierarchy.
