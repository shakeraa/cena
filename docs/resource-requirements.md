# Cena — Resource Requirements & Cost Estimates

This document estimates the compute, storage, memory, third-party services,
and monthly cost required to run the **full Cena application** (web + mobile
backend, knowledge-graph store, LLM mentoring, diagram generation, auth,
analytics).

All figures are **monthly**, in **USD**, and assume a **minimal but production-
ready** deployment. Prices are based on publicly listed rates as of Q2 2026
(Hetzner, DigitalOcean, AWS, Cloudflare R2, Anthropic API, Clerk, Sentry).

> Re-price every quarter: LLM $/Mtok and managed DB tiers move faster than
> the rest of the stack.

---

## 1. Stack Assumptions

| Layer | Choice (minimal) | Why |
|---|---|---|
| Web frontend | Next.js on Vercel/Cloudflare Pages | SSR + static, cheap at low scale |
| Mobile | React Native (Expo), shared API | One codebase for iOS + Android |
| API backend | FastAPI (Python) or Node.js, containerised | Fits LLM orchestration well |
| Relational DB | PostgreSQL 16 + `pgvector` + Apache AGE | Single store for users, graph, embeddings at low scale |
| Cache / queue | Redis (managed or sidecar) | Session cache, rate limits, job queue |
| Object storage | Cloudflare R2 (S3-compatible) | Diagrams, user uploads; **no egress fees** |
| CDN | Cloudflare (free tier -> Pro) | Static assets, SVG cache |
| Auth | Clerk (or self-hosted Supabase Auth) | Faster to ship; swap at scale |
| LLM — mentor | Claude Sonnet 4.6 (hard) + Haiku 4.5 (easy) | ~80% of traffic can run on Haiku |
| LLM — diagrams | Claude Sonnet 4.6 (SVG gen) + shared cache | Dynamic diagram generation |
| Embeddings | Voyage-3 or OpenAI `text-embedding-3-small` | Cheap, good for graph/annotation search |
| Error / logs | Sentry + Axiom (or Grafana Cloud free) | Small free tiers cover early scale |
| Email / push | Resend + Expo Push | Transactional + streak reminders |

---

## 2. Per-User Workload Model

Assumptions used to size everything below (tune these and the cost scales
linearly):

| Metric | Value | Notes |
|---|---|---|
| MAU / paid user | 1.0 | Treat paid ≈ active |
| DAU / MAU | 0.55 | Engaged learning app; streak mechanic |
| Sessions / user / month | 20 | 5-10 min microlearning, ~5 days/week |
| Mentor LLM calls / session | 15 | Socratic turns + evaluation + next-step picker |
| Avg tokens / call | 2 500 in / 800 out | Includes retrieved graph context |
| Prompt-cache hit rate | 70% | System prompt + student overlay re-used |
| Diagrams generated / user / month | 40 (shared cache hit 75%) | Most concepts hit the shared catalog |
| Storage / user (graph + annotations) | 6 MB | Grows ~0.5 MB/mo after first year |
| Egress / user / month | ~200 MB | API JSON + cached SVG/PNG via CDN |

Derived token usage per active user per month:

- Mentor: 20 × 15 × (2 500 in + 800 out) ≈ **750 k in / 240 k out**
- Diagrams (cache-miss only): 10 × (1 k in + 3 k out) ≈ **10 k in / 30 k out**
- Embeddings: ~**50 k tokens**
- Effective (after 70% input cache discount): ~**260 k in / 270 k out**

With a Haiku/Sonnet 80/20 split and prompt caching:

- Mentor LLM: **≈ $2.20 / user / month**
- Diagram LLM: **≈ $0.45 / user / month**
- Embeddings: **≈ $0.05 / user / month**
- **LLM total: ≈ $2.70 / user / month** (budget $3.50 for safety)

---

## 3. Minimal Footprint (Dev / First 20 Users)

Single-box deployment — useful for pilot, staging, and internal testing.

| Resource | Spec | Provider | Cost / mo |
|---|---|---|---|
| App + DB VM | 4 vCPU / 8 GB / 80 GB NVMe | Hetzner CX32 | $8 |
| Object storage | 10 GB + 1 M ops | Cloudflare R2 | $0.15 |
| CDN | Free tier | Cloudflare | $0 |
| Domain + TLS | .com + Let's Encrypt | Namecheap | $1 |
| Auth | Free tier (≤ 10 k MAU) | Clerk | $0 |
| Error tracking | Developer (free) | Sentry | $0 |
| LLM usage (pilot, 20 users) | — | Anthropic | ~$70 |
| **Total** | | | **≈ $80** |

RAM headroom: 8 GB easily holds Postgres (2 GB), Redis (256 MB), API (1 GB),
Next.js SSR (1 GB), with room for background workers.

---

## 4. Scaled Deployment — Full Cost Table

All tiers assume the per-user model in §2. Infrastructure is sized for **p95
load** (peak ≈ 3× average, bursty around evenings and pre-exam periods).

### 4.1 Compute, Data & Services

| Component | 50 users | 100 users | 200 users | 500 users | 1 000 users |
|---|---|---|---|---|---|
| **App servers (API + SSR)** | 1 × 4 vCPU / 8 GB | 1 × 4 vCPU / 8 GB | 2 × 4 vCPU / 8 GB | 2 × 8 vCPU / 16 GB | 3 × 8 vCPU / 16 GB (autoscale) |
| **Postgres (managed)** | shared on app box | 2 vCPU / 4 GB / 40 GB | 2 vCPU / 8 GB / 80 GB | 4 vCPU / 16 GB / 160 GB + 1 replica | 8 vCPU / 32 GB / 320 GB + 1 replica |
| **Redis** | sidecar | sidecar | 1 GB managed | 2 GB managed | 4 GB managed HA |
| **Object storage (R2)** | 15 GB | 25 GB | 50 GB | 120 GB | 240 GB |
| **CDN egress** | 10 GB | 20 GB | 40 GB | 100 GB | 200 GB |
| **Background worker** | same box | same box | 1 × 2 vCPU / 4 GB | 1 × 4 vCPU / 8 GB | 2 × 4 vCPU / 8 GB |
| **Total vCPU** | 4 | 6 | 14 | 28 | 56 |
| **Total RAM** | 8 GB | 12 GB | 28 GB | 64 GB | 128 GB |
| **Total disk (app+DB+blob)** | ~95 GB | ~150 GB | ~260 GB | ~600 GB | ~1.2 TB |

### 4.2 Monthly Cost Breakdown (USD)

| Line item | 50 | 100 | 200 | 500 | 1 000 |
|---|---:|---:|---:|---:|---:|
| App servers | 8 | 8 | 32 | 90 | 180 |
| Postgres (managed) | 0 | 25 | 55 | 180 | 380 |
| Redis | 0 | 0 | 15 | 30 | 70 |
| Object storage (R2) | 1 | 1 | 2 | 3 | 6 |
| CDN / bandwidth | 0 | 0 | 0 | 10 | 20 |
| Background worker | 0 | 0 | 12 | 24 | 48 |
| Auth (Clerk) | 0 | 0 | 0 | 25 | 25 |
| Error / logs (Sentry + Axiom) | 0 | 0 | 26 | 50 | 100 |
| Email + push | 0 | 0 | 5 | 15 | 25 |
| Backups (DB snapshots) | 2 | 5 | 10 | 25 | 50 |
| **Infra subtotal** | **11** | **39** | **157** | **452** | **904** |
| **LLM + embeddings (~$3.50/user)** | 175 | 350 | 700 | 1 750 | 3 500 |
| **Grand total** | **≈ $186** | **≈ $389** | **≈ $857** | **≈ $2 202** | **≈ $4 404** |
| **Cost per user / month** | $3.72 | $3.89 | $4.29 | $4.40 | $4.40 |

Notes:

- **LLM is the dominant cost from ~50 users onward.** Infra is ~5-20% of total.
- Numbers assume **70% prompt-cache hit** and an **80/20 Haiku/Sonnet split**.
  Without caching, LLM cost roughly doubles. Without the Haiku tier, it triples.
- Costs scale roughly linearly with DAU — if your DAU/MAU is 0.35 instead of
  0.55, multiply LLM line by ~0.65.

---

## 5. Files & Storage Inventory

| Artifact | Size (typical) | Where it lives | Lifecycle |
|---|---|---|---|
| User row + profile | 2 KB | Postgres | forever |
| Student knowledge overlay (nodes + edges + history) | 2-6 MB / user | Postgres (AGE + JSONB) | forever, append-only |
| Annotations (text + embeddings) | ~1 KB + 1.5 KB vector / annotation | Postgres + pgvector | forever |
| Domain graph (shared, per syllabus) | 20-80 MB / syllabus | Postgres | versioned |
| Generated SVG diagrams | 20-200 KB each | R2 + CDN | LRU, 90-day TTL on cold items |
| Concept card PNG fallbacks | 30-100 KB each | R2 + CDN | same as SVGs |
| Session transcripts (compressed) | ~15 KB / session | R2 (cold) or Postgres (hot 30 d) | 30 d hot, 1 y cold, then summarise |
| LLM call logs (for eval) | ~4 KB / call | Axiom / S3 | 30 d |
| DB backups | ~1× DB size | R2 | 7 daily + 4 weekly + 3 monthly |
| Mobile app bundle | 30-60 MB | App stores | per release |
| Web static assets | ~5 MB gzipped | CDN | per release |

Required external secret/config files (not code):

- `.env` / secrets manager entries: `ANTHROPIC_API_KEY`, `DATABASE_URL`,
  `REDIS_URL`, `R2_ACCESS_KEY`, `R2_SECRET`, `CLERK_SECRET_KEY`,
  `SENTRY_DSN`, `RESEND_API_KEY`, `EXPO_ACCESS_TOKEN`, `JWT_SIGNING_KEY`.
- Syllabus seed files (JSON/YAML) — one per country/track (~1-10 MB each).
- Prompt templates and methodology definitions (version-controlled).
- TLS certificates (managed automatically via CDN / Let's Encrypt).

---

## 6. How to Hit the "Minimal" Target

If you want the **cheapest viable** path to 100 users, drop the below from
§4.2 and re-price:

1. **Co-locate Postgres on the app VM** up to ~150 users (saves ~$25/mo).
2. **Skip Clerk**, use Supabase Auth or Lucia (free) — saves $25/mo at 500+.
3. **Use only Haiku 4.5** for mentor turns, fall back to Sonnet only when the
   stagnation detector flags conceptual errors — cuts LLM cost ~40%.
4. **Aggressively cache diagrams** behind Cloudflare and treat the first 500
   generated SVGs per syllabus as a shared global cache — near-zero diagram
   LLM cost after month 2.
5. **Defer managed Redis** until you actually need cross-instance sessions.

Applying all five at 200 users brings the grand total from **~$857** down to
**~$520/mo** (**$2.60/user**), at the cost of more ops work and some latency
hit on cold diagrams.

---

## 7. Scaling Breakpoints to Watch

| Trigger | What changes |
|---|---|
| > 200 concurrent sessions | Need managed Redis and a dedicated worker pool |
| > 5 GB Postgres or > 200 MB/s IOPS | Move graph nodes/edges to a dedicated AGE or Neo4j instance |
| > 10 M LLM calls / month | Negotiate Anthropic enterprise pricing; add batch + prompt-cache audits |
| > 1 000 DAU | Multi-region read replicas, per-region R2 buckets, queue-based ingest |
| Regulated markets (EDU data) | Add SOC 2 tooling, EU-region hosting, DLP on annotations — ~$500-1 500/mo extra |

---

## 8. Summary — What It Costs to Run Cena

| Users | Infra / mo | LLM / mo | **Total / mo** | **$ / user** |
|---:|---:|---:|---:|---:|
| 20 (pilot) | $10 | $70 | **$80** | $4.00 |
| 50 | $11 | $175 | **$186** | $3.72 |
| 100 | $39 | $350 | **$389** | $3.89 |
| 200 | $157 | $700 | **$857** | $4.29 |
| 500 | $452 | $1 750 | **$2 202** | $4.40 |
| 1 000 | $904 | $3 500 | **$4 404** | $4.40 |

At a **$12-15/mo** consumer price point, unit economics stay healthy from day
one; the main risk is **LLM cost drift**, which is managed by the Haiku/Sonnet
split, prompt caching, and shared diagram cache described above.
