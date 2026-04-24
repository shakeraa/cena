# GD-008: Market Decision — Arabic-First 5-Unit Physics Wedge

> **Decision**: Target Arabic-speaking Israeli 11th-12th graders preparing for Bagrut Physics 036
> **Pilot cities**: Nazareth, Umm al-Fahm, Rahat
> **Status**: Decision documented 2026-04-13

## Rationale

1. **Unserved market**: No Arabic-language adaptive physics tutor exists for Bagrut 036
2. **Physics is the hardest 5-unit exam**: 42% failure rate in Arab sector vs 18% Jewish sector
3. **PhET validation**: Finkelstein et al. 2005 showed simulation-based physics outperforms real lab
4. **Regulatory advantage**: Arabic content creation avoids Hebrew-specific compliance issues
5. **Cena's CAS engine** (ADR-0002) handles physics equations natively

## Pilot plan

| Phase | Timeline | Students | Scope |
|-------|----------|----------|-------|
| Alpha | 2026 Q3 | 20 students (5 per city + teacher) | Kinematics + Newton's laws |
| Beta | 2026 Q4 | 100 students (3 schools) | Full mechanics + electricity |
| Launch | 2027 Q1 | Open enrollment | Bagrut 036 complete |

## Content requirements

- 200+ questions covering Bagrut 036 curriculum
- Arabic-first authoring (not translations from Hebrew)
- Physics diagrams via PhysicsDiagramService (FIGURE-005)
- CAS verification for all numerical answers (ADR-0002)

## Success metrics

- Bagrut 036 pass rate improvement ≥ 15pp in pilot schools
- Student retention ≥ 60% weekly active over 3 months
- NPS ≥ 40 from students + teachers
