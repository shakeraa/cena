# Fact-Check Report — Technical Claims in Cena Documentation

> **Date:** 2026-03-26
> **Scope:** architecture-design.md, llm-routing-strategy.md, assessment-specification.md, operations.md, intelligence-layer.md, engagement-signals-research.md
> **Previously verified:** product-research.md

---

## 1. Proto.Actor Claims

### 1a. License: Apache 2.0
**VERIFIED.** Proto.Actor is open source under the Apache 2.0 License.
- Source: [Proto.Actor documentation](https://asynkron.se/docs/protoactor/what-is-protoactor/), [GitHub repo](https://github.com/asynkron/protoactor-dotnet)

### 1b. Roger Johansson created both Akka.NET and Proto.Actor
**VERIFIED.** Roger Johansson is the original creator of Akka.NET and later created Proto.Actor as a next-generation actor framework.
- Source: [Roger Johansson Blog](https://rogerjohansson.blog/2015/07/26/building-a-framework-the-early-akka-net-history/), [.NET Rocks podcast](https://www.dotnetrocks.com/default.aspx?ShowNum=1423)

### 1c. Supports both classic and virtual actors
**VERIFIED.** Proto.Actor supports both classical Erlang/Akka-style actors and Microsoft Orleans-style virtual actors (grains) under a common framework.
- Source: [Proto.Actor documentation](https://proto.actor/)

### 1d. Cross-language support for Go and Kotlin
**VERIFIED.** Proto.Actor supports Go, C#, and Java/Kotlin via gRPC-based Actor Standard Protocol. The Kotlin implementation exists but has lower adoption than Go and C#.
- Source: [GitHub protoactor-go](https://github.com/asynkron/protoactor-go), [GitHub protoactor-kotlin](https://github.com/asynkron/protoactor-kotlin)

### 1e. Akka.NET described as "BSL (expensive)" in comparison table
**INACCURATE — FIXED.** Akka.NET (the .NET port) remains Apache 2.0. It is the original Akka framework (Java/Scala by Lightbend) that moved to BSL in September 2022. The comparison table was corrected to reflect that Akka.NET is Apache 2.0, with a note about Akka JVM's BSL creating ecosystem confusion risk.
- Source: [Petabridge blog on Akka license change](https://petabridge.com/blog/lightbend-akka-license-change/), [Akka.NET Wiki Licenses](https://github.com/akkadotnet/akka.net/wiki/Licenses)

---

## 2. NATS JetStream Claims

### 2a. Synadia Cloud $49/month
**VERIFIED.** Synadia Cloud Starter plan is $49/month.
- Source: [Synadia Cloud Pricing](https://docs.synadia.com/cloud/pricing)

### 2b. NATS does 200K+ msg/sec
**VERIFIED.** With the JetStream persistence layer (replicated file-backed stream), NATS caps out at around 200K msg/sec. Core NATS without persistence can handle millions of msg/sec.
- Source: [NATS FAQ](https://docs.nats.io/reference/faq), [NATS bench docs](https://docs.nats.io/using-nats/nats-tools/nats_cli/natsbench)

### 2c. NATS is a single binary
**VERIFIED.** JetStream provides durable streams, key-value storage, and object storage all inside the same ~20MB binary. No additional dependencies required.
- Source: [NATS.io](https://nats.io/), [NATS docs](https://docs.nats.io)

---

## 3. Neo4j AuraDB Pricing

### 3a. AuraDB Professional at $65-130/month
**INACCURATE (clarified) — FIXED.** The pricing is $65/GB/month, not a flat $65-130 range. A 1GB instance costs ~$65/month; a 2GB instance costs ~$131/month. The documents' $65-130 range is coincidentally accurate for 1-2GB provisioned, but was misleading because it omitted the per-GB pricing model. Corrected to "$65/GB/month (AuraDB Professional, 1-2GB)" in the architecture diagram and cost table.
- Source: [Neo4j Pricing](https://neo4j.com/pricing/)

---

## 4. pgvector Claims

### 4a. pgvector supports HNSW indexes
**VERIFIED.** HNSW (Hierarchical Navigable Small World) indexes have been supported since pgvector 0.5.0 and are the preferred index type for production workloads.
- Source: [pgvector GitHub](https://github.com/pgvector/pgvector), [Crunchy Data blog](https://www.crunchydata.com/blog/hnsw-indexes-with-postgres-and-pgvector)

### 4b. pgvector is production-ready
**VERIFIED with caveats.** pgvector has 8M+ installs and is available on all major managed PostgreSQL platforms (AWS RDS, Google Cloud SQL, Supabase, etc.). Version 0.8.0 added iterative index scans. However, at very large scale (50M+ vectors), HNSW memory bloat and WAL issues during index maintenance are known operational concerns. At Cena's scale (~50K items), this is not a concern.
- Source: [pgvector GitHub](https://github.com/pgvector/pgvector), [PostgreSQL pgvector 0.8.0 release](https://www.postgresql.org/about/news/pgvector-080-released-2952/)

---

## 5. Marten Claims

### 5a. Actively maintained
**VERIFIED.** Marten is actively maintained by Jeremy Miller and the JasperFx team. It is widely regarded as the best event sourcing library for .NET on PostgreSQL in 2025-2026.
- Source: [martendb.io](https://martendb.io/), [GitHub JasperFx/marten](https://github.com/JasperFx/marten)

### 5b. Supports event sourcing + projections on PostgreSQL
**VERIFIED.** Marten provides event streams (append-only), inline and async projections, snapshot storage, and an async daemon for background projection rebuilds, all on PostgreSQL.
- Source: [Marten Event Store docs](https://martendb.io/events/)

### 5c. Supports upcasting
**VERIFIED.** Marten provides robust upcasting capabilities including CLR type transformations (old type to new type) and JSON-based transformations. Upcasting is performed on-the-fly when events are read.
- Source: [Marten Events Versioning](https://martendb.io/events/versioning.html)

---

## 6. SymPy Claims

### 6a. SymPy can do symbolic equivalence checking for math expressions
**VERIFIED.** The standard approach is `simplify(student_expr - reference_expr) == 0`, which is exactly what the assessment specification describes. SymPy also provides `expr.equals(other)` which uses numerical methods after simplification. The document's approach (symbolic check with numeric fallback at 5 random points) is the recommended best practice.
- Source: [SymPy Gotchas docs](https://docs.sympy.org/latest/explanation/gotchas.html), [SymPy Simplification tutorial](https://docs.sympy.org/latest/tutorials/intro-tutorial/simplification.html)

---

## 7. Knowledge Space Theory Claims

### 7a. ALEKS uses 25-30 questions to classify student state
**VERIFIED (but document uses different number by design).** ALEKS uses approximately 25-35 questions to classify a student's knowledge state. Cena's documents describe 10-15 questions, but this is explicitly labeled as "ALEKS-inspired" and operates at the assessment-cluster level (80-120 clusters) rather than individual concepts, which is a valid engineering adaptation that requires fewer questions.
- Source: [ALEKS Knowledge Space Theory](https://www.aleks.com/about_aleks/knowledge_space_theory), [Falmagne paper](https://www.aleks.com/about_aleks/Science_Behind_ALEKS.pdf)

### 7b. KST constrains the state space as described
**VERIFIED.** Knowledge Space Theory uses prerequisite relationships to constrain which subsets of concepts form valid knowledge states. The description in the assessment specification (prerequisite-closed subsets, feasible state enumeration, entropy-based item selection) is accurate and consistent with the ALEKS methodology.
- Source: [Wikipedia: Knowledge space](https://en.wikipedia.org/wiki/Knowledge_space), [ALEKS research papers](https://www.sciencedirect.com/science/article/abs/pii/S0022249621000134)

---

## 8. BKT / pyBKT Claims

### 8a. pyBKT is real and maintained
**VERIFIED.** pyBKT is a Python library maintained at [CAHLR/pyBKT](https://github.com/CAHLR/pyBKT) on GitHub, published on PyPI, and documented in peer-reviewed EDM papers.
- Source: [pyBKT GitHub](https://github.com/CAHLR/pyBKT), [PyPI](https://pypi.org/project/pyBKT/)

### 8b. BKT uses parameters p_learn, p_slip, p_guess, p_known
**VERIFIED.** pyBKT uses: `prior` (P(L0), equivalent to p_known), `learns` (P(T), equivalent to p_learn), `guesses` (P(G), equivalent to p_guess), `slips` (P(S), equivalent to p_slip). pyBKT also supports `forgets` (P(F)) as an extension. The intelligence-layer.md correctly references `p_forget` in the BKT parameter refinement flywheel.
- Source: [pyBKT README](https://github.com/CAHLR/pyBKT), [pyBKT paper](https://arxiv.org/abs/2105.00385)

---

## 9. Half-Life Regression Claims

### 9a. Duolingo open-sourced HLR
**VERIFIED.** Duolingo released the code and data under the MIT License in 2016 at [duolingo/halflife-regression](https://github.com/duolingo/halflife-regression). The paper "A Trainable Spaced Repetition Model for Language Learning" was published at ACL 2016.
- Source: [GitHub duolingo/halflife-regression](https://github.com/duolingo/halflife-regression)

### 9b. Formula p(t) = 2^(-delta/h) is correct
**VERIFIED.** The half-life regression model is defined as `p = 2^(-delta/h)` where p is the probability of recall, delta is time since last seen, and h is the half-life of the memory.
- Source: [GitHub README](https://github.com/duolingo/halflife-regression/blob/master/README.md), [Duolingo blog](https://blog.duolingo.com/how-we-learn-how-you-learn/)

---

## 10. SignalR + React Native

### 10a. @microsoft/signalr npm package works with React Native
**INACCURATE — FIXED.** The `@microsoft/signalr` npm package does not officially support React Native. It throws errors when the HttpConnection class detects a non-browser environment. Community wrappers (`react-signalr`, `react-native-signalr`) or transport polyfills are required. A note was added to architecture-design.md Section 11.1.
- Source: [GitHub issue dotnet/aspnetcore#60606](https://github.com/dotnet/aspnetcore/issues/60606), [react-signalr npm](https://www.npmjs.com/package/react-signalr)

---

## 11. react-native-vision-camera

### 11a. Is it really at v4+?
**VERIFIED.** The latest version is 4.7.3 (as of March 2026), with V5 in active development. The `engagement-signals-research.md` reference to "v4+" is accurate.
- Source: [npm react-native-vision-camera](https://www.npmjs.com/package/react-native-vision-camera)

### 11b. Supports frame processors for ML inference
**VERIFIED.** VisionCamera provides frame processor APIs for running ML models on camera frames. Community plugins include react-native-vision-camera-mlkit for face detection, barcode scanning, pose detection, and text recognition. The Frame Processor API adds only ~1ms overhead compared to fully native implementations.
- Source: [VisionCamera Frame Processors docs](https://react-native-vision-camera.com/docs/guides/frame-processors)

---

## 12. EU AI Act Article 5(1)(f)

### 12a. Prohibits emotion inference in education
**VERIFIED.** Article 5(1)(f) of the EU AI Act explicitly prohibits "the placing on the market, the putting into service for this specific purpose, or the use of AI systems to infer emotions of a natural person in the areas of workplace and education institutions, except where the use of the AI system is intended to be put in place or into the market for medical or safety reasons." This became applicable on February 2, 2025. Penalties are up to 35 million euros or 7% of global annual turnover.
- Source: [EU AI Act Article 5](https://artificialintelligenceact.eu/article/5/), [Wolters Kluwer analysis](https://legalblogs.wolterskluwer.com/global-workplace-law-and-policy/the-prohibition-of-ai-emotion-recognition-technologies-in-the-workplace-under-the-ai-act/)

---

## 13. Grafana Cloud Free Tier

### 13a. Covers initial monitoring needs
**VERIFIED.** The Grafana Cloud free tier includes 10K active metrics series, 50GB logs, 50GB traces, 3 users, and 14-day retention. This is sufficient for a <10K user deployment. The document's claim of "10K metrics, 50GB logs, 50GB traces/month" is accurate.
- Source: [Grafana Pricing](https://grafana.com/pricing/), [Grafana Cloud usage limits](https://grafana.com/docs/grafana-cloud/cost-management-and-billing/manage-invoices/understand-your-invoice/usage-limits/)

---

## 14. CodePush

### 14a. CodePush available for React Native hotfixes
**OUTDATED — FIXED.** Microsoft App Center (including CodePush) was retired on March 31, 2025. The technology continues through alternatives: EAS Updates (Expo), Bitrise CodePush (GA as of March 2026), and the self-hosted open-source CodePush server. The operations.md hotfix and rollback sections were updated to reference EAS Updates / Bitrise CodePush instead of the retired Microsoft CodePush.
- Source: [Expo blog: What to do without CodePush](https://expo.dev/blog/what-to-do-without-codepush), [Bitrise CodePush announcement](https://bitrise.io/blog/post/introducing-codepush-beta-ship-react-native-updates-in-minutes)

---

## Summary

| # | Claim | Verdict | Action |
|---|-------|---------|--------|
| 1a | Proto.Actor Apache 2.0 | VERIFIED | None |
| 1b | Roger Johansson created both | VERIFIED | None |
| 1c | Classic + virtual actors | VERIFIED | None |
| 1d | Go + Kotlin cross-language | VERIFIED | None |
| 1e | Akka.NET has BSL | **INACCURATE** | Fixed in architecture-design.md |
| 2a | Synadia Cloud $49/month | VERIFIED | None |
| 2b | NATS 200K+ msg/sec | VERIFIED | None |
| 2c | NATS single binary | VERIFIED | None |
| 3a | Neo4j AuraDB $65-130/month | **INACCURATE** (pricing is per-GB) | Fixed in architecture-design.md |
| 4a | pgvector HNSW indexes | VERIFIED | None |
| 4b | pgvector production-ready | VERIFIED (with scale caveats) | None |
| 5a | Marten actively maintained | VERIFIED | None |
| 5b | Marten event sourcing + projections | VERIFIED | None |
| 5c | Marten upcasting | VERIFIED | None |
| 6a | SymPy symbolic equivalence | VERIFIED | None |
| 7a | ALEKS 25-30 questions | VERIFIED (doc uses 10-15 by design) | None |
| 7b | KST state space constraint | VERIFIED | None |
| 8a | pyBKT real and maintained | VERIFIED | None |
| 8b | BKT parameters | VERIFIED | None |
| 9a | Duolingo open-sourced HLR | VERIFIED | None |
| 9b | HLR formula correct | VERIFIED | None |
| 10a | SignalR + React Native | **INACCURATE** (not officially supported) | Fixed in architecture-design.md |
| 11a | vision-camera v4+ | VERIFIED | None |
| 11b | Frame processors for ML | VERIFIED | None |
| 12a | EU AI Act Article 5(1)(f) | VERIFIED | None |
| 13a | Grafana Cloud free tier | VERIFIED | None |
| 14a | CodePush available | **OUTDATED** (retired March 2025) | Fixed in operations.md |

**Total: 24 claims checked. 20 verified. 4 required corrections (all fixed).**
