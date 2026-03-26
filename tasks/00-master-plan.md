# Cena Platform — Master Task Plan

## Structure
Each domain has its own task file with strict acceptance criteria.
Tasks are ordered by dependency — later tasks depend on earlier ones.

## Domain Files
| File | Domain | Technology | Tasks |
|------|--------|-----------|-------|
| `01-data-layer.md` | Data | PostgreSQL, Marten v7, Neo4j, Redis | 12 tasks |
| `02-actor-system.md` | Backend | Proto.Actor .NET 9, Event Sourcing | 14 tasks |
| `03-llm-layer.md` | LLM | Python FastAPI, Claude, Kimi | 10 tasks |
| `04-mobile-app.md` | Mobile | Flutter/Dart, Riverpod, Drift | 16 tasks |
| `05-frontend-web.md` | Frontend | React, TypeScript, GraphQL | 8 tasks |
| `06-infrastructure.md` | DevOps | AWS, NATS, CI/CD, Monitoring | 10 tasks |
| `07-content-pipeline.md` | Content | Neo4j, Kimi batch, Expert review | 6 tasks |
| `08-security-compliance.md` | Security | GDPR, Auth, PII, Pen testing | 8 tasks |

## Stages
1. **Foundation** (Weeks 1-4): Data layer + actor skeleton + LLM ACL
2. **Core Loop** (Weeks 5-8): Session flow + BKT + item selection + offline sync
3. **Intelligence** (Weeks 9-12): Stagnation detection + methodology switching + MCM
4. **Mobile** (Weeks 5-12): Flutter app, parallel with backend
5. **Polish** (Weeks 13-16): Gamification, accessibility, Arabic, A/B testing
6. **Launch Prep** (Weeks 17-18): Load testing, security audit, Hebrew LLM quality gate

## Acceptance Criteria Standard
Every task MUST have:
- [ ] **Definition of Done** — specific, testable condition
- [ ] **Test** — automated test that proves the criteria is met
- [ ] **Contract reference** — link to the contract file it implements
- [ ] **Blocked by** — explicit dependencies on other tasks
