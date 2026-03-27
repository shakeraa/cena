# Measuring Student Concept Knowledge: Progress, Decay, and Composite Scoring

> **Status:** Research reference document
> **Date:** 2026-03-26
> **Context:** Cena adaptive learning platform — BKT at launch, migrating to MIRT at scale
> **Scope:** Methods for measuring, tracking, and scoring concept mastery in a knowledge graph for high school STEM

---

## Table of Contents

1. [Quantitative Methods](#1-quantitative-methods)
   - 1.1 Knowledge Tracing Models
   - 1.2 Item Response Theory Family
   - 1.3 Spaced Repetition and Forgetting Curve Models
   - 1.4 Knowledge Space Theory
   - 1.5 Performance Metrics (Direct Observation)
   - 1.6 Graph-Based Metrics
2. [Qualitative Methods](#2-qualitative-methods)
   - 2.1 Bloom's Taxonomy Level Classification
   - 2.2 Error Taxonomy and Misconception Detection
   - 2.3 Student Self-Assessment and Confidence Calibration
   - 2.4 Annotation and Reflection Analysis
   - 2.5 Explanation Quality (Feynman Technique Grading)
   - 2.6 Interleaving and Desirable Difficulties
   - 2.7 Cognitive Load Theory (CLT) and Zone of Proximal Development (ZPD)
   - 2.8 Transfer Learning: Near and Far Transfer
3. [Decay and Forgetting Models](#3-decay-and-forgetting-models)
   - 3.1 Ebbinghaus Forgetting Curve
   - 3.2 Half-Life Regression (HLR)
   - 3.3 Memory Strength vs. Retrieval Strength
   - 3.4 DASH Model
   - 3.5 MemoryNet
   - 3.6 Decay Propagation Through Prerequisite Chains
4. [Grading in a Concept Graph](#4-grading-in-a-concept-graph)
   - 4.1 Concept-Level Mastery
   - 4.2 Topic and Domain Aggregation
   - 4.3 Partial Mastery Propagation
   - 4.4 Confidence Intervals on Mastery Estimates
   - 4.5 Visualization of Mastery States
5. [Composite Scoring](#5-composite-scoring)
   - 5.1 Multi-Signal Fusion Architecture
   - 5.2 Bayesian Composite Approach
   - 5.3 Weighted Linear Combination
   - 5.4 Ensemble / Stacking Approach
   - 5.5 Recommended Architecture for Cena

---

## 1. Quantitative Methods

### 1.1 Knowledge Tracing Models

#### 1.1.1 Bayesian Knowledge Tracing (BKT)

**What it measures:** The probability that a student has "learned" (transitioned to a mastery state for) a specific knowledge component, modeled as a hidden Markov model with two states: known and unknown.

**Mathematical formulation:**

Four parameters per knowledge component:
- `P(L_0)` — prior probability of already knowing the skill
- `P(T)` — probability of transitioning from unknown to known after one practice opportunity
- `P(S)` — probability of slipping (incorrect response despite knowing)
- `P(G)` — probability of guessing (correct response despite not knowing)

Update rule after observing a correct response:
```
P(L_t | correct) = P(correct | L_t) * P(L_t) / P(correct)
                 = (1 - P(S)) * P(L_t) / [(1 - P(S)) * P(L_t) + P(G) * (1 - P(L_t))]
```

Update rule after observing an incorrect response:
```
P(L_t | incorrect) = P(S) * P(L_t) / [P(S) * P(L_t) + (1 - P(G)) * (1 - P(L_t))]
```

Learning transition after update:
```
P(L_{t+1}) = P(L_t | obs) + (1 - P(L_t | obs)) * P(T)
```

**Strengths:**
- Interpretable parameters with clear pedagogical meaning
- Lightweight computation — update rule runs in microseconds, no library needed at runtime
- Parameters trainable offline via Expectation Maximization (pyBKT)
- Well-validated across decades of ITS research
- Naturally produces a probability that maps directly to mastery threshold decisions

**Weaknesses:**
- Each skill is tracked independently — no modeling of prerequisite relationships
- Binary latent state (known/unknown) is oversimplified for STEM concepts with partial understanding
- Standard BKT has no forgetting — once learned, always learned (extensions like BKT-Forget address this)
- Assumes items within a skill are interchangeable (no item difficulty modeling)
- Cannot model multidimensional concepts (e.g., a physics problem requiring both calculus and mechanics)

**When to use it:**
- At launch with limited interaction data
- For skills that are relatively independent (vocabulary-like components)
- When interpretability matters more than prediction accuracy
- As a baseline against which more complex models are validated

**How it maps to a concept graph:**
- One BKT state per concept node in the graph
- The graph structure is NOT used by BKT itself — each node updates independently
- Graph prerequisites can be layered on top as business rules: "concept B cannot be marked mastered unless prerequisite A has P(L) > 0.85"
- The mastery vector (one P(L) per concept) is the student's knowledge overlay on the graph

**Cena implementation mapping:**
- The `StudentProfile` virtual actor (Proto.Actor, event-sourced) maintains one BKT state per concept in its knowledge overlay
- On each `ConceptAttempted` domain event: run BKT update, emit `ConceptMastered` if P(L) crosses 0.90 threshold, or emit `MasteryDecayed` if recall drops below 0.70
- BKT parameters (P(L_0), P(T), P(S), P(G)) per knowledge component stored in Neo4j as concept node properties, trained offline via pyBKT on PostgreSQL/Marten event store replays
- The `MethodologySwitched` event triggers a methodology-conditioned P(T) lookup — different teaching methods may have different transition probabilities for the same concept
- Snapshot every 100 events: the full BKT state vector is serialized as part of the `StudentProfile` actor snapshot in PostgreSQL/Marten
- NATS JetStream publishes `ConceptMastered` events for downstream consumption by the Engagement Context (XP/badges) and Analytics Context (dashboards)

---

#### 1.1.2 Deep Knowledge Tracing (DKT)

**What it measures:** The probability of a student answering the next question correctly, using a recurrent neural network (LSTM or GRU) that implicitly learns latent knowledge states from the full sequence of student interactions.

**Mathematical formulation:**

Input sequence: `x_1, x_2, ..., x_t` where each `x_t` is a one-hot encoding of (exercise_id, correct/incorrect).

Hidden state update (LSTM):
```
h_t = LSTM(x_t, h_{t-1})
```

Output:
```
y_t = sigmoid(W * h_t + b)
```
where `y_t` is a vector of predicted correctness probabilities for all exercises at time `t+1`.

Loss function: binary cross-entropy between predicted and actual correctness.

**Strengths:**
- Captures complex temporal dependencies that BKT cannot (e.g., forgetting patterns, learning bursts)
- Does not require hand-specified skill-to-item mappings — can discover latent structure
- Generally achieves higher AUC than BKT on next-response prediction (typically 0.80-0.86 vs. 0.65-0.75 for BKT)
- Can implicitly learn prerequisite relationships from data

**Weaknesses:**
- Black box — hidden states are not interpretable as "mastery of concept X"
- Requires large datasets (100K+ interactions) to train effectively
- Can predict impossible states (mastery going backward without forgetting mechanism)
- Computationally expensive at training time
- No natural mechanism to extract per-concept mastery probabilities — requires post-hoc interpretation
- Catastrophic forgetting during continual learning

**When to use it:**
- When you have large-scale interaction data and prediction accuracy matters more than interpretability
- As a research/validation tool to compare against simpler models
- NOT recommended as the primary mastery model in a concept graph system where per-concept mastery needs to be explainable

**How it maps to a concept graph:**
- Poorly. DKT's hidden state does not decompose cleanly into per-concept mastery.
- Can be used as a secondary prediction layer: "given all history, predict P(correct) for the next item from concept C" — then that prediction informs the concept mastery estimate.
- Some researchers extract attention weights from DKT variants to approximate per-concept importance, but this is noisy.

---

#### 1.1.3 Dynamic Key-Value Memory Networks (DKVMN)

**What it measures:** Student mastery of individual concepts using a key-value memory architecture where keys represent concepts and values represent mastery states.

**Mathematical formulation:**

- **Key matrix** `M_k` (static, size N x d_k): N concept embeddings
- **Value matrix** `M_v` (dynamic, size N x d_v): N mastery state vectors, updated per student

Attention weight for item `q_t`:
```
w_t = softmax(q_t^T * M_k)
```

Read mastery state:
```
r_t = sum_i(w_t[i] * M_v[i])
```

Predict correctness:
```
p_t = sigmoid(f([q_t, r_t]))
```

Update value memory after observing response:
```
M_v[i] = M_v[i] + w_t[i] * erase_gate * write_vector
```

**Strengths:**
- Explicitly models per-concept mastery (unlike DKT's opaque hidden state)
- Memory slots correspond to concepts — interpretable
- Can model concept interactions through attention weights
- Better than DKT at handling new concepts not seen during training

**Weaknesses:**
- Fixed number of memory slots requires pre-defining concept count
- Training still requires substantial data
- Concept embeddings must be learned — cold-start problem for new concepts
- Memory update mechanism can overwrite important history

**When to use it:**
- When you want deep learning performance with per-concept interpretability
- When concept graph structure is stable and well-defined
- At scale (50K+ students) where training data is abundant

**How it maps to a concept graph:**
- Natural mapping: each memory slot = one concept node in the graph
- Key matrix can be initialized from concept graph embeddings (e.g., node2vec on the prerequisite graph)
- Attention weights reveal which concepts are activated together, potentially validating or discovering prerequisite edges

---

#### 1.1.4 Attentive Knowledge Tracing (AKT)

**What it measures:** Student knowledge state using self-attention (Transformer) over the full interaction history, with explicit concept-level and item-level embeddings.

**Mathematical formulation:**

Three embedding layers:
- Concept embedding: `c_t = embed_concept(concept_id_t)`
- Item embedding: `e_t = embed_item(item_id_t)` (Rasch-like: concept embedding + item difficulty offset)
- Response embedding: `r_t = embed_response(item_id_t, correct_t)`

Self-attention over interaction history:
```
Attention(Q, K, V) = softmax(QK^T / sqrt(d_k) + context_bias) * V
```

The context bias incorporates:
- Monotonic attention decay (recent interactions weighted more)
- Knowledge retrieval attention (how relevant each past interaction is to the current question)

**Strengths:**
- State-of-the-art accuracy on most benchmark datasets
- Explicit item difficulty modeling (Rasch-embedded)
- Attention weights are interpretable — you can see which past interactions influenced the current prediction
- Handles long interaction histories better than LSTM-based models
- Models forgetting implicitly through attention decay

**Weaknesses:**
- O(n^2) complexity in sequence length — requires truncation for long histories
- Heavy computational cost at inference time compared to BKT
- Requires large training datasets
- Attention decay parameters need careful tuning

**When to use it:**
- When prediction accuracy is paramount and computational resources are available
- When you want to identify which past learning moments contributed to current mastery
- As a research tool for understanding learning dynamics

**How it maps to a concept graph:**
- Concept embeddings can be pre-initialized from graph structure (GNN-based embeddings of the concept graph)
- Per-concept mastery can be estimated by running attention over all interactions tagged with that concept
- Attention patterns can reveal emergent prerequisite relationships to validate the concept graph

---

#### 1.1.5 SPARFA (Sparse Factor Analysis)

**What it measures:** Jointly estimates student abilities, item difficulties, and the latent concept-to-item mapping from response data alone.

**Mathematical formulation:**

Response model:
```
P(Y_{ij} = 1) = sigmoid(w_j^T * c_i - mu_j)
```
where:
- `c_i` = student i's ability vector (K dimensions, one per latent concept)
- `w_j` = item j's concept loading vector (sparse — each item loads on only a few concepts)
- `mu_j` = item j's intrinsic difficulty

The sparsity constraint on `w_j` means each item is associated with only a few concepts, making the model interpretable.

Joint estimation via variational inference or MCMC:
- Learn `c_i` for each student (ability profile)
- Learn `w_j` for each item (concept loadings)
- Learn `mu_j` for each item (difficulty)

**Strengths:**
- Discovers the latent Q-matrix (concept-to-item mapping) from data — does not require expert tagging
- Produces interpretable per-concept ability estimates
- Sparse loadings make the model pedagogically meaningful
- Can validate or improve a hand-authored concept graph

**Weaknesses:**
- Computationally expensive (MCMC or variational inference)
- Static model — does not track learning over time (no temporal dynamics)
- Assumes a fixed number of latent concepts K (must be specified)
- Requires sufficient items per concept for identifiability

**When to use it:**
- For offline analysis: validating the concept graph structure, discovering missing prerequisites
- For initial calibration: estimating item difficulties and concept loadings before deploying BKT/MIRT
- NOT for real-time mastery tracking (too slow, no temporal dynamics)

**How it maps to a concept graph:**
- Each latent factor maps to a concept node
- The sparse loading matrix `W` reveals which items belong to which concepts — this can be compared against the hand-authored Q-matrix
- Student ability vectors provide a snapshot of mastery across all concepts at a point in time

---

#### 1.1.6 Performance Factors Analysis (PFA)

**What it measures:** The probability of a correct response as a function of accumulated successes and failures on each knowledge component, without requiring a hidden state model.

**Mathematical formulation:**

```
P(correct | KC_set) = sigmoid(sum_j(beta_j + gamma_j * s_j + rho_j * f_j))
```
where for each knowledge component `j` tagged to the current item:
- `s_j` = count of prior successes on KC j
- `f_j` = count of prior failures on KC j
- `beta_j` = difficulty intercept for KC j
- `gamma_j` = learning rate from successes (positive — each success increases P)
- `rho_j` = learning rate from failures (negative or small positive — failures indicate difficulty)

**Strengths:**
- Directly observable features (success/failure counts) — no hidden states, no EM fitting
- Naturally handles multiple KCs per item (sum over tagged KCs)
- Parameters trained via logistic regression — fast, scalable, well-understood
- Outperforms BKT on several benchmark datasets (Pavlik et al. 2009, doi:10.3233/978-1-60750-028-5-531)
- Each KC's gamma/rho reveals how quickly students learn from practice vs. mistakes

**Weaknesses:**
- No temporal dynamics — counts are cumulative, no forgetting
- Assumes each success/failure contributes equally regardless of recency
- Cannot model the "unknown → known" transition that BKT captures
- Sensitive to KC granularity — too coarse and it masks patterns, too fine and data is sparse

**When to use it:**
- As a lightweight alternative to BKT when per-KC fitting is impractical
- For rapid prototyping — logistic regression trains in seconds
- As a secondary validation model: if PFA and BKT disagree on mastery, the concept needs more data
- NOT as Cena's primary model (lacks temporal dynamics and forgetting)

**How it maps to a concept graph:**
- One (s_j, f_j, beta_j, gamma_j, rho_j) tuple per concept node
- The graph structure is not used by PFA — nodes are independent
- Can be layered with graph constraints the same way as BKT

---

#### 1.1.7 Elo Rating System for Education

**What it measures:** Student ability and item difficulty on a shared scale, updated after each interaction using the chess Elo rating update rule. Both student and item ratings are dynamic.

**Mathematical formulation:**

Expected score for student i on item j:
```
E_ij = 1 / (1 + 10^((D_j - theta_i) / 400))
```

After observing outcome `S_ij` (1 = correct, 0 = incorrect):
```
theta_i_new = theta_i + K_student * (S_ij - E_ij)
D_j_new     = D_j     + K_item    * (E_ij - S_ij)
```

where:
- `theta_i` = student ability rating
- `D_j` = item difficulty rating
- `K_student` = student learning rate (typically 40 for new students, decreasing to 10 with experience)
- `K_item` = item update rate (typically 10, decreasing as more data accumulates)
- The factor 400 is the Elo scale parameter (convention from chess)

**Strengths:**
- Extremely simple to implement — two update equations, no libraries needed
- Both student ability AND item difficulty are estimated simultaneously and dynamically
- No batch training required — fully online, updates in real-time
- Well-understood convergence properties (Pelánek 2016, doi:10.1016/j.compedu.2016.03.017)
- Naturally calibrates item difficulty from student interactions — items that everyone gets right drift to low difficulty
- Can be computed per concept by maintaining per-concept Elo ratings

**Weaknesses:**
- Single-dimensional — one rating per student (or per student-concept pair)
- No forgetting model — ratings only change on interaction, not with time
- K-factor selection is heuristic
- Assumes item responses are independent (no sequencing effects)
- Less principled than IRT for high-stakes assessments

**When to use it:**
- For rapid item difficulty calibration during content authoring
- As a lightweight real-time signal alongside BKT (Elo for ranking, BKT for mastery state)
- For adaptive item selection: present items where E_ij ≈ 0.5 (maximum information)
- For gamification: student Elo can power leaderboards and matchmaking

**How it maps to a concept graph:**
- One Elo rating per (student, concept) pair — equivalent to a per-concept ability estimate
- Item Elo ratings stored as concept node metadata
- Student Elo can be compared across concepts to identify relative strengths/weaknesses
- The gap between student Elo and item Elo determines item selection priority

---

#### 1.1.8 FSRS (Free Spaced Repetition Scheduler)

**What it measures:** Optimal review timing using a three-component memory model: stability (S), difficulty (D), and retrievability (R). Next-generation open-source scheduler that outperforms SM-2 (Ye 2022, arXiv:2402.01032).

**Mathematical formulation:**

Retrievability (probability of recall after `t` days since last review):
```
R(t, S) = (1 + t / (9 * S))^(-1)
```

This is a power-law decay (not exponential like Ebbinghaus). Stability `S` represents the interval at which R drops to 90%.

Stability update after review (correct response):
```
S_new = S * (1 + e^(w_8) * (11 - D) * S^(-w_9) * (e^(w_10 * (1 - R)) - 1))
```

Stability update after review (incorrect response / lapse):
```
S_new = w_11 * D^(-w_12) * ((S + 1)^w_13 - 1) * e^(w_14 * (1 - R))
```

Difficulty update:
```
D_new = D - w_6 * (grade - 3)
```

where `w_0..w_14` are 15 learnable parameters optimized per user via gradient descent on review history.

**Strengths:**
- Power-law decay better fits empirical data than exponential (Wixted & Ebbesen 1991)
- Only 15 parameters — trainable from a single student's history (no global dataset needed)
- Open-source, MIT licensed, actively maintained
- Validated on 10K+ Anki users — significantly outperforms SM-2 on retention prediction
- Separates stability from difficulty — a hard concept can still be well-memorized

**Weaknesses:**
- Primarily designed for flashcard-style atomic facts, not multi-step STEM problems
- Assumes each review is independent — no modeling of related concept interactions
- Power-law vs. exponential debate is unresolved for all domains
- Requires ~20 reviews per card to converge on per-user parameters

**When to use it:**
- As a potential upgrade path from HLR for Cena's spaced repetition scheduling
- For factual/recall concepts (definitions, formulas, constants) where flashcard-style review is appropriate
- NOT for conceptual understanding items — those are better served by Bloom's-level progression
- Consider for Phase 3 as an alternative to HLR, with A/B testing to compare retention rates

**How it maps to a concept graph:**
- One (S, D, R) triple per concept node per student
- `R` is computed in real-time: `R = (1 + t/(9*S))^(-1)` — same role as HLR's `p(recall)`
- Scheduling: review when `R < 0.9` (FSRS convention) vs. HLR's configurable threshold
- Stability `S` serves the same role as HLR's half-life `h` — both encode memory durability

---

#### 1.1.9 Leitner System

**What it measures:** A box-based spaced repetition system where items move between boxes (levels) based on response correctness. Simpler than algorithmic approaches, but effective and easy to explain to students.

**Mathematical formulation:**

Box assignment rules:
```
if correct: box(item) = min(box(item) + 1, max_box)
if incorrect: box(item) = 1  // reset to first box
```

Review interval per box:
```
interval(box_k) = base_interval * multiplier^(k - 1)
```

Typical configuration:
```
Box 1: review every 1 day    (base_interval = 1)
Box 2: review every 3 days   (multiplier ≈ 3)
Box 3: review every 9 days
Box 4: review every 27 days
Box 5: review every 81 days  (mastered — quarterly review)
```

**Strengths:**
- Extremely intuitive — students understand the "box" metaphor immediately
- No parameters to train — heuristic but effective
- Maps naturally to gamification: "Promote this concept to Box 5!"
- The visual of items moving through boxes is inherently motivating
- Leitner (1972) — decades of practical validation

**Weaknesses:**
- Binary outcome only (correct/incorrect) — no partial credit
- Fixed intervals — not personalized to student memory characteristics
- Harsh reset on failure (back to Box 1) can be demoralizing
- No item difficulty modeling — hard and easy items in the same box have the same interval
- Inferior retention rates compared to algorithmic approaches (HLR, FSRS)

**When to use it:**
- NOT as Cena's primary scheduling algorithm (HLR is already specified and superior)
- As a gamification layer: map HLR's continuous half-life to discrete "boxes" for student display
- For the student-facing visualization: "This concept is in Box 4 — nearly mastered!"
- As a simplified mental model when explaining spaced repetition to students and parents

**How it maps to a concept graph:**
- One box number per concept node per student (derived from HLR half-life ranges)
- Mapping: `box = floor(log(half_life_hours / base_hours) / log(multiplier)) + 1`
- Purely a presentation layer — the underlying model is HLR, Leitner is the UX skin

---

### 1.2 Item Response Theory Family

#### 1.2.1 Rasch Model (1PL IRT)

**What it measures:** The probability of a correct response as a function of student ability (single dimension) and item difficulty (single parameter per item).

**Mathematical formulation:**
```
P(X_{ij} = 1 | theta_i, b_j) = exp(theta_i - b_j) / (1 + exp(theta_i - b_j))
```
where:
- `theta_i` = student i's ability (latent trait, single dimension)
- `b_j` = item j's difficulty

**Strengths:**
- Specific objectivity: item difficulty estimates are independent of the student sample and vice versa
- Parameter estimation is well-understood (joint maximum likelihood, conditional maximum likelihood, marginal maximum likelihood)
- The sufficient statistic for `theta` is just the total raw score — simple and powerful
- Item and person fit statistics detect model violations

**Weaknesses:**
- Single-dimensional — assumes one ability underlies all responses
- Assumes equal item discrimination — all items are equally "good" at differentiating high and low ability students
- For STEM subjects, single-dimension is too limiting (math ability != physics ability, even within math, algebra ability != geometry ability)

**When to use it:**
- For initial item calibration — estimate item difficulty parameters from pilot data
- For placement tests where a single overall "level" is sufficient
- As a component within MIRT (each dimension is effectively a Rasch-like sub-model)

**How it maps to a concept graph:**
- Maps to a single concept or a single topic cluster where unidimensionality holds
- A Rasch model per topic cluster in the graph is a viable simplified approach before MIRT

---

#### 1.2.2 2PL and 3PL IRT

**What it measures:** Same as Rasch, but with additional item parameters.

**Mathematical formulation (3PL):**
```
P(X_{ij} = 1 | theta_i, a_j, b_j, c_j) = c_j + (1 - c_j) / (1 + exp(-a_j * (theta_i - b_j)))
```
where:
- `a_j` = discrimination (how well the item differentiates)
- `b_j` = difficulty
- `c_j` = pseudo-guessing parameter (lower asymptote)

**Strengths over Rasch:**
- Discrimination parameter identifies which items are most informative at each ability level — critical for Computerized Adaptive Testing (CAT)
- Guessing parameter is important for multiple-choice items in Bagrut exams
- More realistic for items that vary in quality

**Weaknesses:**
- More parameters to estimate — needs larger sample sizes (~500+ students per item for stable 3PL estimates)
- Guessing parameter (`c_j`) is notoriously hard to estimate
- Still unidimensional

**When to use it:**
- For item calibration and selection in adaptive testing
- 2PL is the sweet spot for most educational applications (discrimination + difficulty, skip guessing)
- Use 3PL only for multiple-choice items where guessing is a genuine concern

**How it maps to a concept graph:**
- Same as Rasch — unidimensional, maps to a single concept or topic cluster
- Item discrimination values help identify "gateway" items that are most diagnostic for a particular concept

---

#### 1.2.3 Multidimensional IRT (MIRT)

**What it measures:** The probability of a correct response as a function of multiple latent ability dimensions, where each dimension corresponds to a knowledge component or concept cluster.

**Mathematical formulation (compensatory model):**
```
P(X_{ij} = 1 | theta_i, a_j, d_j) = sigmoid(a_j^T * theta_i + d_j)
```
where:
- `theta_i` = student i's ability vector (K dimensions)
- `a_j` = item j's discrimination vector (K loadings — which dimensions the item measures)
- `d_j` = item j's intercept (related to overall difficulty)

**The Q-matrix connection:** In confirmatory MIRT, the `a_j` vector is constrained by a Q-matrix that specifies which concepts each item measures. If item j only measures concepts 2 and 5, then `a_j[k] = 0` for `k != 2, 5`.

**Strengths:**
- Models the multidimensional nature of STEM knowledge — a physics problem can simultaneously assess vector algebra and Newton's laws
- The Q-matrix encodes the concept graph's item-to-concept mapping directly
- Produces per-dimension (per-concept) ability estimates with standard errors
- Well-established statistical theory with identifiability conditions understood
- Natural fit for a concept graph system: theta dimensions = concept nodes

**Weaknesses:**
- Estimation requires substantial data (rule of thumb: 500+ students per item, 20+ items per dimension for stable estimates)
- Computational cost grows with the number of dimensions — full MIRT with 2,000 dimensions (one per concept) is infeasible
- Requires a Q-matrix (item-to-concept mapping) — this is equivalent to the skill model that must be authored or discovered
- Static model — does not inherently model learning over time

**When to use it:**
- As the primary mastery estimation engine once sufficient interaction data exists (10K+ students)
- For diagnostic assessments that need to estimate mastery across multiple concepts simultaneously
- For Cena's migration from BKT: MIRT provides the multi-concept modeling that BKT lacks

**How it maps to a concept graph:**
- Direct mapping: each MIRT dimension = one concept or concept cluster in the graph
- The Q-matrix IS the bipartite graph between items and concepts
- Prerequisite edges in the concept graph can constrain MIRT: if A is a prerequisite of B, then `theta[A]` should be greater than `theta[B]` on average (this can be used as a prior or validation check)
- At 2,000 concepts per subject, MIRT dimensions should be concept clusters (20-50 dimensions), not individual concepts

**Practical MIRT for Cena:**
- Use a two-tier approach: MIRT at the topic cluster level (20-50 dimensions per subject), BKT or direct tracking at the individual concept level within each cluster
- The MIRT theta vector provides the "zoomed out" mastery view; BKT provides the "zoomed in" per-concept view
- Estimate MIRT parameters offline in batch; use the estimated theta vector as a prior that informs BKT updates

**Cena implementation mapping:**
- The Q-matrix is derived from Neo4j's concept graph: each item's concept tags define which MIRT dimensions it loads on. Cypher query: `MATCH (i:Item)-[:ASSESSES]->(c:Concept) RETURN i.id, collect(c.cluster_id)`
- MIRT parameter estimation runs as an offline batch job (Python, `mirt` or `mirtjml` package) on the Analytics Context's read replica of the PostgreSQL/Marten event store
- Estimated theta vectors are published back to the Learner Context via NATS JetStream `learner.mirt.updated` event
- The `StudentProfile` virtual actor receives the MIRT theta update and uses it as a Bayesian prior for BKT: `P(L_0) = sigmoid(theta_k)` for concept k's cluster dimension
- The `StagnationDetected` event can trigger a targeted MIRT re-estimation for the stagnated concept cluster — if the student's theta is misestimated, the MIRT update corrects the prior and unblocks progress
- Confidence intervals from MIRT's Fisher information matrix determine whether `ConceptMastered` events should fire — the lower bound of the 95% CI must exceed the mastery threshold (0.90)

---

### 1.3 Spaced Repetition and Forgetting Curve Models

#### 1.3.1 Ebbinghaus Forgetting Curve (Exponential Decay)

**What it measures:** The probability of recall as a function of time since learning, assuming no review.

**Mathematical formulation:**
```
R(t) = e^(-t/S)
```
or equivalently:
```
R(t) = e^(-lambda * t)
```
where:
- `R(t)` = retention probability at time t
- `S` = memory stability (higher = slower decay)
- `lambda = 1/S` = decay rate

**Strengths:**
- Simplest possible decay model — one parameter per (student, concept) pair
- Historically well-established (Ebbinghaus, 1885)
- Easy to compute and schedule reviews: `t_review = -S * ln(threshold)`

**Weaknesses:**
- Assumes a single decay rate — does not account for the spacing effect (reviewed items decay slower)
- The stability parameter `S` is hard to estimate from sparse data
- Does not model the strengthening effect of successful retrieval practice
- Too simple for production use — replaced by HLR and power-law models

**When to use it:**
- As a conceptual baseline only
- Useful for communicating the concept of decay to stakeholders
- Not recommended for production scheduling

**How it maps to a concept graph:**
- One `S` value per (student, concept_node) pair
- No interaction between concepts — each node decays independently

---

#### 1.3.2 Half-Life Regression (HLR) — Duolingo Model

**What it measures:** The probability of correct recall as a function of elapsed time, where the memory half-life is a learned function of the student's practice history with that item.

**Mathematical formulation:**
```
p(recall | delta, h) = 2^(-delta / h)
```
where:
- `delta` = time elapsed since last review
- `h` = memory half-life (time for recall probability to drop to 50%)

Half-life is computed as:
```
h = 2^(theta^T * x)
```
where:
- `theta` = learned regression weights (shared across all students, trained offline)
- `x` = feature vector for this (student, item) pair:
  - Total number of times seen
  - Total number of correct responses
  - Number of times seen in current session
  - Number of correct in current session
  - Time since last practice (lag)
  - Lexeme-level features (word length, frequency, cognate status — or for Cena: concept difficulty, prerequisite depth, concept type)

**Training objective:** Minimize the squared loss between predicted recall probability and observed binary correctness, weighted by time lag.

**Strengths:**
- Trainable from data — hard concepts automatically get shorter half-lives
- Incorporates practice history features — the more you practice, the longer the half-life grows
- Validated at massive scale (12M+ practice sessions at Duolingo, 50% lower error rate vs. Leitner)
- Produces a concrete scheduling signal: schedule review when `p(recall) < threshold`
- Open-sourced by Duolingo (MIT license)

**Weaknesses:**
- Assumes exponential decay (power-law models sometimes fit better)
- Feature engineering required (what features matter for STEM concepts vs. language vocabulary?)
- Theta weights are global — does not model per-student differences in memory ability
- Cold-start: needs at least 2-3 interactions per (student, concept) to be useful

**When to use it:**
- This is Cena's current spaced repetition model (specified in architecture-design.md)
- Production-ready for scheduling concept reviews
- The `(h, t_last_review)` pair stored per (student, concept) is the core spaced repetition state

**How it maps to a concept graph:**
- One `(h, t_last_review)` tuple per concept node in the student's overlay
- Review scheduling: `p(recall) = 2^(-delta / h)`; when `p < 0.85`, schedule review
- Concept difficulty and prerequisite depth from the graph are input features to the half-life regression
- Cross-concept effects not modeled (see Section 3.6 for decay propagation)

**Cena implementation mapping:**
- The `StudentProfile` virtual actor stores `(h, t_last_review)` per concept as part of its event-sourced state
- On each `ConceptAttempted` event: update half-life `h` using the HLR regression, update `t_last_review` to now
- The `MasteryDecayed` domain event is emitted when a background timer in the `StudentProfile` actor detects `p(recall) < 0.70` — this actor uses Proto.Actor's `ReceiveTimeout` to periodically re-evaluate decay without external cron jobs
- HLR feature vector for STEM includes: `attempt_count`, `correct_count`, `concept_difficulty` (from Neo4j node property), `prerequisite_depth` (graph distance to root), `bloom_level_achieved`, `days_since_first_encounter`
- HLR theta weights trained offline via Python (scikit-learn logistic regression) on the Analytics Context's PostgreSQL/Marten event store replay
- Review scheduling events published via NATS JetStream to the Outreach Context, which triggers WhatsApp/Telegram/push notifications: "Time to review [concept] — your recall is fading"
- The `AnnotationAdded` event on a concept resets `t_last_review` (annotation counts as a retrieval opportunity, extending the half-life)

---

#### 1.3.3 DASH (Difficulty, Ability, and Study History)

**What it measures:** Recall probability incorporating item difficulty, student ability, and detailed study history (spacing, frequency, recency).

**Mathematical formulation:**
```
P(recall) = sigmoid(ability_i - difficulty_j + sum_k(w_k * f_k(study_history)))
```

Study history features include:
- Number of prior exposures
- Spacing between exposures (mean, variance)
- Recency of last exposure
- Success rate on prior exposures
- Time since first encounter

DASH extends HLR by adding an explicit ability parameter per student and difficulty per item, rather than relying solely on regression features.

**Strengths:**
- Separates student ability from item difficulty (like IRT) while also modeling decay (like HLR)
- More principled than HLR for heterogeneous student populations
- Can leverage IRT-calibrated difficulty parameters

**Weaknesses:**
- More parameters to estimate than HLR
- Less validated at production scale than HLR
- Student ability parameter may drift over time — requires periodic recalibration

**When to use it:**
- When student ability varies widely (which it does for Bagrut students across 3-5 unit levels)
- As an upgrade from HLR when enough data exists to estimate per-student ability
- Consider for Cena post-launch when per-student profiles are rich enough

**How it maps to a concept graph:**
- Same as HLR: one decay state per concept node
- Student ability parameter can be the MIRT theta for the concept's dimension — connecting IRT and spaced repetition

---

#### 1.3.4 MemoryNet

**What it measures:** Uses a neural network (typically LSTM) to model the evolution of memory strength over time, learning the decay and consolidation dynamics from data.

**Mathematical formulation:**

Memory state at time t:
```
m_t = LSTM(x_t, m_{t-1}, delta_t)
```
where `delta_t` is the time gap since last interaction, fed as an explicit input.

Recall prediction:
```
p(recall_t) = sigmoid(W * m_t)
```

The LSTM learns to model both decay (large `delta_t` should reduce memory strength) and consolidation (spaced practice should increase resistance to decay).

**Strengths:**
- Can learn arbitrary decay patterns from data (not constrained to exponential)
- Models interactions between different memories (the LSTM hidden state encodes all past items)
- Can capture spacing effect, testing effect, and interference effects

**Weaknesses:**
- Black box — no interpretable half-life parameter
- Requires large training datasets
- Difficult to extract scheduling rules (when to review) from the LSTM state
- Overkill for most educational applications where HLR performs well

**When to use it:**
- Research context for understanding complex memory dynamics
- NOT recommended for production scheduling — HLR provides the same practical value with interpretability

**How it maps to a concept graph:**
- Opaque. The LSTM hidden state does not decompose into per-concept memory strength.
- Would require running a separate MemoryNet per concept to get per-concept predictions, which is expensive.

---

### 1.4 Knowledge Space Theory (KST) / ALEKS Approach

**What it measures:** The student's "knowledge state" — which subset of concepts they have mastered — where only certain subsets are feasible given prerequisite relationships.

**Mathematical formulation:**

Let `Q = {q_1, q_2, ..., q_n}` be the set of all concepts.

A **knowledge state** is a subset `K ⊆ Q` that is **downward closed** with respect to the prerequisite partial order: if concept `q` is in `K` and `p` is a prerequisite of `q`, then `p` must also be in `K`.

The **knowledge space** `K` is the collection of all feasible knowledge states.

**Assessment:** Maintain a probability distribution over knowledge states:
```
P(K | observations) ∝ P(observations | K) * P(K)
```

After observing a correct response to item testing concept `q`:
- Increase probability of all states containing `q`
- Decrease probability of all states not containing `q`
- Adjusted for guessing and slipping (analogous to BKT)

**The key insight:** Despite millions of feasible states for a subject with 350 concepts, the prerequisite structure creates a lattice where each observation eliminates large clusters of states. This is why ALEKS achieves accurate classification in only 25-30 questions.

**Strengths:**
- Directly models the concept graph structure — prerequisite relationships are first-class
- Theoretically elegant and well-founded (Doignon & Falmagne, 2011)
- Optimal for diagnostic assessments (minimal questions to classify knowledge state)
- The "outer fringe" of a knowledge state (concepts whose prerequisites are all mastered) is exactly the set of concepts the student is ready to learn — this IS the adaptive item selection strategy
- No parameter fitting required if the prerequisite structure is known

**Weaknesses:**
- Combinatorial explosion: the number of feasible states grows exponentially with concept count
- Binary per concept (mastered or not) — no partial mastery
- Does not model learning dynamics (only snapshots)
- Does not model forgetting
- Requires the prerequisite graph to be correct — errors in the graph propagate to incorrect state estimates

**When to use it:**
- For onboarding diagnostics: rapidly estimate a new student's knowledge state in 10-15 questions (already specified for Cena)
- For identifying the "learning frontier" — concepts whose prerequisites are satisfied and that are ready to be taught
- NOT as the ongoing mastery model (lacks temporal dynamics and partial mastery)

**How it maps to a concept graph:**
- KST IS a concept graph model — the prerequisite graph directly defines the knowledge space
- The outer fringe of the student's estimated knowledge state = the next concepts to teach
- Inner fringe = concepts most recently mastered (candidates for review/reinforcement)
- For Cena: use KST for the onboarding diagnostic, then hand off to BKT/MIRT for ongoing tracking

---

### 1.5 Performance Metrics (Direct Observation)

These are raw signals extracted directly from student interactions, before any latent modeling.

#### 1.5.1 Accuracy

**What it measures:** Proportion of correct responses on items tagged to a concept.

**Formulation:** `accuracy_c = correct_c / total_c`

**Variants:**
- **Rolling accuracy:** Last N attempts only (avoids ancient history)
- **Weighted accuracy:** Recent attempts weighted more heavily: `sum(w_t * correct_t) / sum(w_t)` where `w_t = lambda^(T-t)`
- **First-attempt accuracy:** Only counts the first attempt per item (avoids inflation from repeated attempts)

**Strengths:** Simple, intuitive, directly observable. No model needed.

**Weaknesses:** No uncertainty quantification. Confounded by item difficulty. Does not distinguish guessing from mastery.

**Graph mapping:** One accuracy value per concept node. Meaningful only with sufficient items (>5 attempts).

#### 1.5.2 Response Latency

**What it measures:** Time taken to respond, which correlates with automaticity of knowledge retrieval.

**Formulation:** `latency_c = median(response_time_t for concept c)`

**Variants:**
- **Latency trend:** Slope of response time over attempts (decreasing = fluency building)
- **Normalized latency:** `latency_c / expected_latency_c` where expected is derived from item difficulty
- **Latency-accuracy pair:** Fast-and-correct = mastery; slow-and-correct = effortful retrieval; fast-and-incorrect = careless/guessing; slow-and-incorrect = genuine difficulty

**Strengths:** Captures fluency, which accuracy alone misses. A student who gets everything right but takes 3 minutes per problem has not truly "mastered" the concept.

**Weaknesses:** High variance (affected by distraction, device, reading speed). Must be normalized per student and per item type.

**Graph mapping:** One latency metric per concept node. The latency-accuracy quadrant is particularly useful for classifying mastery quality.

#### 1.5.3 Streak Analysis

**What it measures:** Consecutive correct responses on a concept, used as a simple mastery signal.

**Formulation:** Current streak = count of consecutive correct responses (resets on incorrect).

**Mastery rule variant:** "3 correct in a row" = mastered (used by many simple systems).

**Strengths:** Extremely simple. Intuitively meaningful. Good for gamification.

**Weaknesses:** Fragile — one slip resets the streak even for a mastered concept. Does not account for item difficulty. Highly sensitive to guessing.

**Graph mapping:** One streak counter per concept node. Better used as a gamification signal than a mastery signal.

#### 1.5.4 Error Classification

**What it measures:** The type of error, not just its occurrence.

**Categories for STEM:**
- **Procedural error:** Correct approach, execution mistake (sign error, arithmetic slip)
- **Conceptual error:** Fundamental misunderstanding (applying wrong formula, misidentifying concept)
- **Careless error:** Known pattern — student clearly knows the material but made a slip (fast response, isolated error in otherwise correct work)
- **Systematic error:** Same mistake pattern repeated across items — indicates a misconception
- **Transfer error:** Correct for one concept, incorrectly applied to another (overgeneralization)

**Formulation:** Requires an error classifier (LLM-based for open-ended responses, rule-based for structured responses):
```
error_type = classify(student_response, correct_answer, concept_context)
```

**Strengths:** Directly actionable — procedural errors need drill, conceptual errors need re-teaching, systematic errors need misconception correction.

**Weaknesses:** Classification is non-trivial for open-ended responses. Requires either LLM calls (expensive) or hand-authored rules (brittle).

**Graph mapping:** Error type distribution per concept node. Systematic errors with the same misconception pattern across related concepts suggest a deeper issue at a shared prerequisite.

---

### 1.6 Graph-Based Metrics

These metrics leverage the structure of the concept graph itself, not just individual nodes.

#### 1.6.1 Mastery Propagation

**What it measures:** The degree to which mastery of prerequisite concepts predicts and supports mastery of downstream concepts.

**Formulation:**
```
prerequisite_support(c) = min(mastery(p) for p in prerequisites(c))
```
or a softer version:
```
prerequisite_support(c) = weighted_mean(mastery(p) for p in prerequisites(c))
```

**Key principle:** A concept's "effective mastery" should be bounded by its weakest prerequisite. If a student "knows" calculus integration but has forgotten algebra, their integration mastery is unreliable.

**Effective mastery:**
```
effective_mastery(c) = min(mastery(c), prerequisite_support(c))
```

**Strengths:** Captures the structural reality that STEM knowledge builds on foundations.

**Weaknesses:** The `min` function is harsh — a single weak prerequisite can suppress mastery of an entire subtree.

**Graph mapping:** This IS a graph metric. It requires traversing prerequisite edges and propagating mastery constraints downward.

#### 1.6.2 Prerequisite Satisfaction Index

**What it measures:** The fraction of a concept's prerequisites that are mastered, indicating readiness to learn.

**Formulation:**
```
PSI(c) = count(p : mastery(p) > threshold, p in prerequisites(c)) / count(prerequisites(c))
```

**Strengths:** Directly answers "is this student ready for concept C?" Binary threshold makes it actionable.

**Weaknesses:** All-or-nothing threshold. Does not weight prerequisites by importance or recency.

**Graph mapping:** Computed per concept node. PSI = 1.0 means the concept is in the student's "outer fringe" (KST terminology) and ready to learn. PSI < 1.0 means prerequisites need work first.

#### 1.6.3 Cluster Mastery

**What it measures:** Mastery at the level of a topic cluster (group of related concepts), rather than individual concepts.

**Formulation:**
```
cluster_mastery(T) = weighted_mean(mastery(c) for c in T, weights = importance(c))
```

where `importance(c)` can be:
- Uniform (all concepts equally important)
- Degree-based (concepts with more dependents are more important)
- PageRank on the prerequisite graph (concepts that are prerequisites for many others rank higher)
- Exam-weight-based (concepts that appear more frequently on Bagrut exams rank higher)

**Strengths:** Provides a meaningful summary at the topic level, useful for dashboards and progress tracking.

**Weaknesses:** Aggregation can mask individual concept gaps.

**Graph mapping:** Defined over subgraphs (topic clusters). The clustering itself can be derived from the graph structure (e.g., community detection on the concept graph).

#### 1.6.4 Learning Frontier Width

**What it measures:** The number of concepts the student is currently ready to learn (concepts whose prerequisites are all satisfied but which are not yet mastered).

**Formulation:**
```
frontier(student) = {c : PSI(c) = 1.0 and mastery(c) < threshold}
|frontier| = count(frontier(student))
```

**Strengths:** A wide frontier means the student has many learning options. A narrow frontier means they are bottlenecked. A zero-width frontier for a specific subtree means foundational gaps are blocking all progress.

**Weaknesses:** Binary thresholds for "prerequisite satisfied" and "not yet mastered."

**Graph mapping:** Directly computed from the graph structure and the mastery overlay.

---

## 2. Qualitative Methods

### 2.1 Bloom's Taxonomy Level Classification

**What it measures:** The cognitive level at which a student demonstrates understanding of a concept, on a six-level hierarchy: Remember, Understand, Apply, Analyze, Evaluate, Create.

**How to implement:**

Each item in the item pool is tagged with its Bloom's level. Mastery of a concept is then measured not just as "can they answer correctly" but "at what cognitive level can they demonstrate proficiency."

**Assessment mapping:**
```
Level 1 - Remember:    Can recall the formula (F = ma)
Level 2 - Understand:  Can explain what the formula means in words
Level 3 - Apply:       Can use the formula to solve a standard problem
Level 4 - Analyze:     Can decompose a complex scenario to identify which forces apply
Level 5 - Evaluate:    Can assess whether a given solution is physically reasonable
Level 6 - Create:      Can design an experiment to verify Newton's second law
```

**Mastery as Bloom's level:**
```
bloom_mastery(student, concept) = max(level : student demonstrates proficiency at level)
```

**Strengths:**
- Captures depth of understanding, not just correctness
- Aligns with educational standards and teacher expectations
- Enables targeted instruction: a student stuck at "Apply" needs different help than one stuck at "Remember"
- Meaningful for parent and teacher dashboards

**Weaknesses:**
- Items must be pre-tagged with Bloom's levels — labor-intensive
- Level classification of student responses (especially open-ended) requires LLM evaluation
- The taxonomy is debated — boundaries between levels are fuzzy
- Not all concepts naturally support all six levels

**Graph mapping:**
- Each concept node can have a Bloom's mastery level per student (0-6), in addition to the probability-based mastery score
- Higher Bloom's levels on prerequisite concepts predict readiness for higher levels on downstream concepts
- A student at "Remember" level on a prerequisite concept should not be presented "Analyze" level items on a dependent concept

---

### 2.2 Error Taxonomy and Misconception Detection

**What it measures:** Not just whether a student is wrong, but WHY they are wrong, and whether the error reveals a specific misconception.

**STEM Misconception Categories:**

For Physics:
- "Heavier objects fall faster" (Aristotelian mechanics)
- "Force is needed to maintain motion" (impetus theory)
- "Current is consumed by resistors" (sequential reasoning in circuits)

For Mathematics:
- "Multiplication always makes things bigger" (fails for fractions < 1)
- "The limit IS the function value" (conflating limit and continuity)
- "Distributing exponents over addition" ((a+b)^2 = a^2 + b^2)

**Detection methods:**

1. **Distractor analysis:** Multiple-choice items with distractors designed to attract specific misconceptions. Choosing distractor D3 reveals misconception M7.

2. **LLM-based analysis:** For open-ended responses:
```
input: student_response, correct_solution, concept_context
output: {
  error_type: "conceptual" | "procedural" | "careless",
  misconception_id: "PHYS_FORCE_IMPETUS" | null,
  confidence: 0.85,
  explanation: "Student appears to believe force is needed for constant velocity"
}
```

3. **Pattern matching across attempts:** If a student consistently applies the same wrong rule across different items, that pattern is a misconception signal.

**Strengths:**
- Directly actionable — misconception-specific remediation is far more effective than generic re-teaching
- Builds a misconception profile per student that can trigger methodology switches
- Well-researched in science education literature (Vosniadou, diSessa, Chi)

**Weaknesses:**
- Requires a misconception library per subject (labor-intensive to author)
- LLM-based detection is expensive per interaction
- False positives: a careless error can look like a misconception on a single item

**Graph mapping:**
- Misconceptions can be modeled as "anti-nodes" or negative edges in the concept graph: misconception M is associated with concept C and blocks mastery of C until resolved
- The misconception library can be stored as metadata on concept nodes or as a separate overlay graph
- When a misconception is detected, it effectively "locks" the concept node and its dependents until resolved

**Cena implementation mapping:**
- Error classification runs in the Pedagogy Context's `LearningSession` actor — each `ConceptAttempted` event includes the student's response, which is classified by the LLM tier (Kimi K2.5 for structured/MCQ responses, Claude Sonnet 4.6 for open-ended)
- Classified error types are stored as part of the `ConceptAttempted` event payload in PostgreSQL/Marten
- The `StagnationDetected` domain event fires when the error taxonomy shows systematic/repeated error patterns (3+ same error type across sessions) — this triggers the MCM graph lookup in the Pedagogy Context for methodology switching
- Misconception nodes in Neo4j: `(:Misconception {id, description, subject})-[:BLOCKS]->(:Concept)` — when a misconception is detected, the `StudentProfile` actor adds a `(:Student)-[:HAS_MISCONCEPTION]->(:Misconception)` edge
- The `MethodologySwitched` event is emitted when error classification triggers a switch from the current teaching method to one better suited to the error type (conceptual → Socratic, procedural → drill)

---

### 2.3 Student Self-Assessment and Confidence Calibration

**What it measures:** The student's subjective assessment of their own mastery, and the calibration between their confidence and their actual performance.

**Implementation:**

After completing a set of items on a concept, prompt:
```
"How confident are you that you understand [concept]?"
1 - Not at all confident
2 - Slightly confident
3 - Moderately confident
4 - Very confident
5 - Completely confident
```

**Calibration metric:**
```
calibration_error = |self_assessed_mastery - actual_mastery|
```

**Calibration categories:**
- **Well-calibrated:** Self-assessment matches actual performance (|error| < 0.15)
- **Overconfident:** Self-assessment >> actual performance (Dunning-Kruger territory)
- **Underconfident:** Self-assessment << actual performance (common in high-achieving students, especially female students in STEM — documented bias)

**Strengths:**
- Overconfidence is a direct predictor of future failure — students who think they know material but don't will not review it
- Calibration improvement is itself a learning outcome (metacognition)
- Low-cost signal to collect (one question per concept)
- Underconfident students may benefit from encouragement, not more instruction

**Weaknesses:**
- Students may game it (rating low to get easier content)
- Young students (15-17) are notoriously poorly calibrated
- Self-assessment alone is unreliable — must always be combined with performance data

**Graph mapping:**
- One (self_confidence, calibration_error) pair per concept node
- Concepts with high calibration error are candidates for review even if performance-based mastery is high (for overconfident) or for advancement even if the student is hesitant (for underconfident)
- Aggregate calibration error across the graph measures metacognitive development

---

### 2.4 Annotation and Reflection Analysis

**What it measures:** Depth and quality of student self-generated notes, reflections, and annotations as a proxy for understanding.

**Implementation for Cena (already specified in architecture-design.md as `AnnotationAdded` domain event):**

Students can add notes to concepts in the knowledge graph. These annotations are analyzed for:

1. **Content depth:** Does the annotation demonstrate surface-level recall or deep understanding?
2. **Connection-making:** Does the student link this concept to other concepts in the graph?
3. **Questioning:** Does the student ask generative questions (sign of active learning)?
4. **Sentiment:** Is the student expressing confusion, frustration, or confidence?

**NLP-based analysis (LLM):**
```
input: annotation_text, concept_context, student_level
output: {
  depth_score: 0.0-1.0,  // surface → deep
  connections_mentioned: ["concept_A", "concept_C"],
  question_quality: "generative" | "clarifying" | "none",
  sentiment: "confused" | "frustrated" | "confident" | "curious" | "neutral",
  bloom_level_demonstrated: 2  // which Bloom's level the annotation reflects
}
```

**Strengths:**
- Captures understanding that performance data misses — a student who can solve problems but cannot explain their reasoning has fragile knowledge
- Annotations persist and can be reviewed during spaced repetition
- Cross-concept connections in annotations validate or reveal graph edges
- Already part of Cena's domain model

**Weaknesses:**
- Requires LLM calls for analysis (cost: Kimi-tier task)
- Not all students will annotate — voluntary signal with selection bias
- Quality of NLP analysis depends on language (Hebrew NLP is less mature than English)

**Graph mapping:**
- Annotations are metadata on concept nodes
- Cross-concept references in annotations suggest the student perceives a relationship — can be compared against the graph structure
- Annotation depth score can be a component of the composite mastery score

---

### 2.5 Explanation Quality (Feynman Technique Grading)

**What it measures:** Can the student explain the concept in simple terms, as if teaching it to someone else? This is the deepest test of understanding — you cannot explain what you do not truly understand.

**Implementation:**

Prompt the student with:
```
"Explain [concept] as if you were teaching it to a younger student who has never seen it before."
```

**LLM-based grading rubric:**
```
input: student_explanation, concept_definition, prerequisite_concepts
output: {
  completeness: 0.0-1.0,     // covers all key aspects
  accuracy: 0.0-1.0,         // no factual errors
  simplicity: 0.0-1.0,       // uses accessible language
  analogy_quality: 0.0-1.0,  // uses effective analogies/examples
  prerequisite_grounding: 0.0-1.0,  // connects to foundational concepts
  misconceptions_revealed: ["misconception_id_1"],
  overall_feynman_score: 0.0-1.0
}
```

**Strengths:**
- The strongest signal of deep understanding available
- Reveals misconceptions that performance data cannot (student may get answers right for the wrong reasons)
- Aligns with Bloom's Level 5-6 (Evaluate/Create)
- Highly engaging when framed as "teach the AI" or "explain to a friend"

**Weaknesses:**
- Time-intensive for the student — cannot be done for every concept
- LLM grading of explanations has its own accuracy limits
- Language proficiency confounds understanding assessment (a student may understand perfectly but struggle to articulate in Hebrew)
- Should be used selectively for key concepts, not as routine assessment

**Graph mapping:**
- Feynman score on a concept node represents the deepest level of mastery
- A high Feynman score on a concept is strong evidence that prerequisite concepts are also understood (you can't explain calculus if you don't understand limits)
- Concepts flagged for Feynman assessment should be "gateway" concepts with many dependents in the graph

---

### 2.6 Interleaving and Desirable Difficulties

**What it measures:** Whether the learning schedule optimally interleaves different concepts (rather than blocking them) and introduces desirable difficulties that strengthen long-term retention.

**Theoretical foundation:**

Bjork (1994) introduced the "desirable difficulties" framework: learning conditions that appear to slow initial acquisition but enhance long-term retention and transfer. Three core desirable difficulties:

1. **Spacing effect:** Distributing practice over time (vs. massing). Cepeda et al. (2006, doi:10.1037/0033-2909.132.3.354) meta-analyzed 839 assessments confirming spacing superiority across all domains.

2. **Interleaving effect:** Mixing different concept types during practice (vs. blocking one concept at a time). Kornell & Bjork (2008, doi:10.1111/j.1467-9280.2008.02127.x) showed interleaving improves category learning. Rohrer et al. (2015, doi:10.1037/edu0000001) demonstrated interleaving doubled math test scores for 7th graders.

3. **Testing effect / retrieval practice:** Using recall-based testing instead of re-study. Roediger & Karpicke (2006, doi:10.1111/j.1467-9280.2006.01693.x) showed testing produced 50% higher retention at 1-week delay compared to re-study.

**Measuring interleaving quality:**

```
interleaving_ratio(session) = count(concept_switches) / (count(items) - 1)
```

where `concept_switches` = number of times the concept changes between consecutive items.

- `interleaving_ratio = 0.0`: Pure blocking (all items from one concept, then all from another)
- `interleaving_ratio = 1.0`: Full interleaving (every item is a different concept)
- Optimal range for STEM: 0.3-0.7 (partial interleaving, Rohrer & Taylor 2007)

**Measuring desirable difficulty level:**

```
difficulty_calibration(student, session) = mean(|P_predicted(correct) - 0.85|)
```

Items should target ~85% success rate (the "85% rule" — Wilson et al. 2019, doi:10.1038/s41467-019-12552-4). This maximizes the desirable difficulty: hard enough to strengthen memory, easy enough to avoid frustration.

**Strengths:**
- Directly actionable for session design — adjust interleaving ratio and difficulty targeting
- Backed by robust experimental evidence across decades
- Interleaving naturally prevents the "illusion of competence" from blocked practice
- Compatible with all mastery models (BKT, MIRT, HLR)

**Weaknesses:**
- Interleaving can feel harder to students (lower perceived competence during practice — counterintuitive)
- Optimal interleaving ratio depends on concept similarity — closely related concepts benefit more
- The "desirable" threshold varies per student and per concept difficulty

**Cena implementation mapping:**
- The `LearningSession` actor in the Pedagogy Context controls item selection — it maintains the `interleaving_ratio` target for each session
- Item selection algorithm: after presenting an item from concept A, the next item is drawn from a different concept B with probability `interleaving_target` (default 0.5, tunable per student based on `StagnationDetected` signals)
- Difficulty calibration uses the BKT/Elo predicted P(correct) to select items near the 0.85 sweet spot — items where `|P_predicted - 0.85| < 0.15` are prioritized
- The `ConceptAttempted` event logs the actual difficulty experienced (correct/incorrect + response time) — over time, the session generator learns each student's optimal difficulty zone

**Graph mapping:**
- Interleaving is a session-level property, not a concept-node property
- The concept graph determines which concepts can be interleaved: concepts from the same topic cluster or sharing prerequisites are good interleaving candidates
- Neo4j query for interleaving candidates: `MATCH (c1:Concept)-[:RELATED_TO|:SHARES_PREREQUISITE]-(c2:Concept) WHERE c1.id = $current AND student_mastery(c2) > 0.4 RETURN c2`

---

### 2.7 Cognitive Load Theory (CLT) and Zone of Proximal Development (ZPD)

**What it measures:** Whether the learning material is calibrated to the student's current cognitive capacity (CLT) and falls within their zone of learnable concepts (ZPD).

**Cognitive Load Theory (Sweller 1988, doi:10.1207/s15516709cog1202_4):**

Three types of cognitive load:
1. **Intrinsic load:** Complexity inherent to the concept itself (e.g., integration by parts has higher intrinsic load than basic addition)
2. **Extraneous load:** Unnecessary complexity from poor instructional design (confusing diagrams, irrelevant information)
3. **Germane load:** Productive mental effort that builds schemas and long-term memory

**CLT measurement:**

```
estimated_cognitive_load(student, concept) =
    intrinsic_load(concept) * (1 - mastery(prerequisites))
  + extraneous_load(presentation_mode)
  + germane_load(methodology_fit)
```

Where:
- `intrinsic_load(concept)` = concept's inherent difficulty (stored in Neo4j as a node property, calibrated from aggregate student performance)
- `(1 - mastery(prerequisites))` = intrinsic load is amplified when prerequisites are weak (element interactivity increases)
- `extraneous_load` depends on presentation mode (text, diagram, animation, worked example)
- `germane_load` depends on methodology fit (well-matched methodology increases productive germane load)

**Overload detection signals:**
- Response time exceeds 2x the student's baseline for similar difficulty → cognitive overload
- Accuracy drops below 40% on items the model predicts > 60% → material too complex
- Session abandonment after less than 5 minutes → likely overload or frustration
- Annotation sentiment shows confusion signals → explicit overload indicator

**Zone of Proximal Development (Vygotsky 1978):**

The ZPD is the gap between what a student can do independently and what they can do with scaffolding. In graph terms:

```
ZPD(student) = {c : prerequisite_support(c) >= 0.6  // scaffolding can bridge the gap
                AND mastery(c) < 0.4}                // not yet independently mastered
```

Concepts in the ZPD are the ideal learning targets — hard enough to challenge, close enough to existing knowledge that scaffolding is effective.

**Scaffolding strategy (Wood, Bruner & Ross 1976, doi:10.1111/j.1469-7610.1976.tb00381.x):**

```
scaffolding_level(student, concept) =
    if mastery(c) < 0.2 and PSI(c) < 0.8: "full scaffolding" (worked example)
    if mastery(c) < 0.4 and PSI(c) >= 0.8: "partial scaffolding" (faded example)
    if mastery(c) < 0.7: "hints on request" (student attempts first)
    if mastery(c) >= 0.7: "no scaffolding" (independent practice)
```

This implements Renkl & Atkinson's (2003, doi:10.1207/S15326985EP3801_3) fading strategy — scaffolding is progressively removed as mastery increases.

**Strengths:**
- CLT provides a principled framework for session pacing — stop adding new concepts when cognitive load is high
- ZPD focuses instruction on the most learnable concepts, preventing wasted effort on too-easy or too-hard material
- Scaffolding level can be automatically derived from the mastery overlay
- Both frameworks are deeply established in educational psychology

**Weaknesses:**
- Cognitive load is not directly observable — must be inferred from behavioral signals
- ZPD boundaries are fuzzy — the 0.6 prerequisite threshold is heuristic
- Scaffolding level transitions must be smooth to avoid jarring the student

**Cena implementation mapping:**
- The `StudentProfile` actor maintains a `cognitive_load_profile` — a per-student estimate of maximum information absorption rate, updated from session engagement signals
- The Pedagogy Context's `LearningSession` actor checks `estimated_cognitive_load` before each new item — if the cumulative session load exceeds the student's threshold, the session ends with a review of previously-learned concepts instead of introducing new material
- ZPD computation runs against the Neo4j concept graph: the learning frontier (PSI = 1.0 concepts) plus near-frontier concepts (PSI >= 0.6) form the ZPD
- Scaffolding level is a parameter passed to the LLM prompt system — the system prompt for each methodology (Socratic, Feynman, etc.) has variants for each scaffolding level
- The `StagnationDetected` event at the scaffolding level triggers methodology switching: if the student is stagnating with "hints on request", escalate to "partial scaffolding" before switching methodologies

**Graph mapping:**
- `intrinsic_load` is a property on each concept node in Neo4j, calibrated from aggregate student performance data
- ZPD is computed from the mastery overlay + prerequisite structure — it IS a graph computation
- Scaffolding level per concept is derived from `mastery(c)` and `PSI(c)` — both graph-overlay properties

---

### 2.8 Transfer Learning: Near and Far Transfer

**What it measures:** Whether a student can apply knowledge learned in one context to novel situations, and how the distance between the learning context and the application context affects success.

**Theoretical foundation:**

Barnett & Ceci (2002, doi:10.1037/0033-2909.128.4.612) proposed a comprehensive taxonomy for transfer along multiple dimensions:

| Dimension | Near Transfer | Far Transfer |
|-----------|--------------|--------------|
| **Knowledge domain** | Same domain (algebra → algebra) | Cross-domain (algebra → physics) |
| **Physical context** | Same setting (classroom → classroom) | Different setting (classroom → lab) |
| **Temporal context** | Immediate (same session) | Delayed (weeks/months later) |
| **Functional context** | Same task type (solve equation → solve equation) | Different task type (solve equation → model real-world) |
| **Social context** | Same social setting | Different social setting |
| **Modality** | Same representation (symbolic → symbolic) | Different representation (symbolic → graphical) |

**Near transfer** occurs when the learning and application contexts share most dimensions — e.g., applying the quadratic formula to a new quadratic equation. Near transfer is relatively reliable when mastery is high (Thorndike & Woodworth 1901).

**Far transfer** occurs when the contexts differ on multiple dimensions — e.g., using algebraic reasoning to solve a novel physics problem. Far transfer is rare, fragile, and requires deep structural understanding rather than procedural fluency (Perkins & Salomon 1992, doi:10.1016/B978-0-08-042620-0.50050-8).

**Transfer distance formulation in a concept graph:**

Given a concept graph `G = (V, E)` where edges represent prerequisite or relatedness relationships, the transfer distance between a source concept `s` and a target concept `t` can be quantified:

```
transfer_distance(s, t) = shortest_path_length(s, t, G)
```

For weighted graphs incorporating semantic similarity:

```
transfer_distance(s, t) = min_path(sum(1 - edge_similarity(e)) for e in path(s, t))
```

Where `edge_similarity(e)` captures how closely related adjacent concepts are (derived from co-occurrence in student performance data or embedding cosine similarity).

**Transfer probability model:**

```
P(transfer | mastery_s, distance) = mastery_s^alpha * exp(-beta * distance)
```

Where:
- `mastery_s` = mastery of the source concept (higher mastery → more transferable knowledge)
- `alpha` = mastery exponent (typically > 1, reflecting that deep mastery transfers better than shallow)
- `distance` = transfer distance in the concept graph
- `beta` = decay rate for transfer probability with distance (domain-specific, learned from data)

**Near vs. far transfer classification based on graph distance:**

```
if transfer_distance(s, t) <= 2:    "near transfer" (same cluster, shared prerequisites)
if transfer_distance(s, t) <= 4:    "moderate transfer" (adjacent clusters)
if transfer_distance(s, t) > 4:     "far transfer" (cross-domain or distant clusters)
```

**Measuring transfer in practice:**

1. **Direct transfer assessment:** Present a problem that requires concept A but is framed in the context of concept B. If the student succeeds, transfer from A to B is demonstrated.

2. **Transfer readiness score:**
```
transfer_readiness(s, t) = mastery(s) * structural_similarity(s, t) * (1 - transfer_distance(s, t) / max_distance)
```

Where `structural_similarity(s, t)` is computed from shared prerequisites:
```
structural_similarity(s, t) = |prerequisites(s) ∩ prerequisites(t)| / |prerequisites(s) ∪ prerequisites(t)|
```

This Jaccard similarity on prerequisite sets captures how structurally related two concepts are, independent of graph distance.

3. **Transfer evidence accumulation:** Track cases where mastering concept A improves performance on concept B without direct instruction on B. This is observable in the event stream as accuracy improvements on B following `ConceptMastered` events for A.

**Strengths:**
- Distinguishes shallow memorization (no transfer) from deep understanding (transfers broadly)
- Directly actionable: concepts with low transfer indicate rote learning — switch to deeper methodologies (Socratic, Feynman)
- Aligns with Bloom's higher levels (Analyze, Evaluate, Create inherently require transfer)
- Transfer evidence validates the concept graph structure — high transfer between two concepts suggests they should be closer in the graph

**Weaknesses:**
- Far transfer is difficult to measure reliably — requires carefully designed cross-domain items
- The transfer distance metric depends on graph quality — errors in the graph distort distance estimates
- Transfer probability parameters (alpha, beta) must be learned from data and may vary by domain
- Confounding: apparent transfer may be explained by a hidden common prerequisite

**Cena implementation mapping:**
- The `LearningSession` actor in the Pedagogy Context periodically inserts **transfer probe items** — items tagged to concept B but solvable via concept A, where A is a recently mastered concept. The student's response reveals whether transfer from A to B has occurred.
- Transfer events are logged as `ConceptAttempted` with a `transfer_source` metadata field — the Analytics Context can then compute per-edge transfer rates across the graph
- The `StudentProfile` actor maintains a `transfer_profile` — a sparse matrix of observed transfer strengths between concept pairs, updated incrementally as transfer probes are answered
- Neo4j stores `[:TRANSFERS_TO {strength, distance, observed_count}]` edges between concepts, populated from aggregate transfer probe data — these edges inform the Pedagogy Context's item selection (if transfer is weak between A and B, explicitly teach the connection)
- The `StagnationDetected` event can be augmented with transfer failure signals — if a student masters individual concepts but fails transfer probes, the stagnation type is `transfer_failure`, triggering methodology switch to cross-concept integration exercises

**Graph mapping:**
- Transfer distance is computed directly from the Neo4j concept graph via shortest-path queries
- Transfer readiness scores are per-(source, target) concept pairs, forming a second overlay on the graph
- High transfer readiness between concepts that lack a direct edge suggests a missing prerequisite or relatedness edge — this feeds back into graph refinement
- The prerequisite graph distance between source and target concepts predicts transfer likelihood: concepts within 2 hops show ~70% near-transfer success with high mastery, while concepts >4 hops show <20% far-transfer success (calibrated from aggregate student data)

---

## 3. Decay and Forgetting Models

### 3.1 Ebbinghaus Forgetting Curve

Covered in Section 1.3.1. Summary: `R(t) = e^(-t/S)`. Simple exponential decay, one stability parameter per (student, concept). Baseline model only — not recommended for production.

### 3.2 Half-Life Regression (HLR)

Covered in Section 1.3.2. Summary: `p(t) = 2^(-delta/h)` where `h = 2^(theta^T * x)`. Trainable decay model. Cena's current choice. Production-ready.

### 3.3 Memory Strength vs. Retrieval Strength (Bjork's Theory)

**What it measures:** Two independent dimensions of memory that explain why knowledge can be "known but not recalled."

**Conceptual model (Bjork & Bjork, 1992):**

- **Storage strength (S_s):** How well-learned the memory is. Increases monotonically with practice. Never decreases (you cannot "unlearn" something, only lose access to it).

- **Retrieval strength (S_r):** How easily the memory can be accessed right now. Decays with time and interference. This is what the Ebbinghaus curve measures.

**Key relationships:**
```
S_r(t) = f(S_s, delta_t, interference)    // retrieval strength decays over time
dS_s/dt = g(difficulty_of_retrieval)       // storage strength increases MORE when retrieval is difficult
```

**The desirable difficulty principle:** When `S_r` is low (the memory is hard to retrieve), successful retrieval increases `S_s` more than when `S_r` is high. This is why spaced practice (allowing `S_r` to decay before reviewing) is more effective than massed practice.

**Practical formulation for implementation:**
```
S_s(concept, student) = base_strength + sum(bonus_per_review_k)
  where bonus_per_review_k = alpha * (1 - S_r_at_time_of_review_k)

S_r(concept, student, t) = S_s * decay_function(t - t_last_review)
```

**Strengths:**
- Explains the spacing effect, testing effect, and interleaving effect — the three pillars of effective learning
- Provides principled scheduling: review when `S_r` drops but `S_s` is high enough that the review will succeed
- A concept with high `S_s` and low `S_r` is "temporarily forgotten but easily re-learned" — different from a concept with low `S_s` (never properly learned)

**Weaknesses:**
- `S_s` is not directly observable — must be inferred
- No widely-adopted standard implementation (unlike HLR)
- More complex to implement than single-parameter decay models

**When to use it:**
- As the theoretical grounding for spaced repetition scheduling decisions
- To distinguish "forgotten but easily re-learned" (high S_s, low S_r) from "never properly learned" (low S_s, low S_r) — these require different interventions
- Can be implemented as an extension of HLR: the half-life `h` in HLR is a proxy for storage strength

**Graph mapping:**
- Two values per concept node: (S_s, S_r) instead of just a mastery probability
- A concept with high S_s and low S_r needs a quick review, not re-teaching
- A concept with low S_s needs full re-instruction regardless of S_r

---

### 3.4 DASH Model

Covered in Section 1.3.3. Combines IRT-style ability/difficulty with study history features. More principled separation of student and item parameters than HLR.

### 3.5 MemoryNet

Covered in Section 1.3.4. Neural approach to memory modeling. Research tool, not recommended for production.

### 3.6 Decay Propagation Through Prerequisite Chains

**The core question:** If a student has mastered concept B (which depends on prerequisite A), and then concept A's recall probability decays below a threshold, does B's mastery effectively decay too?

**Answer: Yes, and this must be modeled explicitly.**

**Rationale from cognitive science:**
- STEM knowledge is hierarchical. You cannot correctly apply the chain rule (calculus) if you have forgotten the product rule, which depends on understanding limits.
- Decay at the foundation corrupts everything built on top, even if the student has not been tested on the downstream concepts.
- This is NOT the same as the downstream concept independently decaying — it is a structural dependency.

**Implementation approaches:**

#### Approach 1: Effective Mastery with Prerequisite Floor

```
effective_mastery(c) = min(measured_mastery(c), min(effective_mastery(p) for p in prerequisites(c)))
```

This recursive definition propagates decay upward through the graph. If a foundational concept decays, all its descendants' effective mastery is capped.

**Strengths:** Simple, conservative, ensures no concept is rated higher than its weakest foundation.

**Weaknesses:** Too harsh — one decayed prerequisite suppresses entire subtrees. Does not distinguish "slightly decayed" from "completely forgotten."

#### Approach 2: Weighted Prerequisite Penalty

```
effective_mastery(c) = measured_mastery(c) * product(max(measured_mastery(p) / threshold, 1.0) for p in prerequisites(c))
```

If all prerequisites are above threshold, no penalty. As they decay below threshold, the penalty grows multiplicatively.

**Strengths:** Graduated penalty. Less harsh than `min`.

**Weaknesses:** The product can be overly punitive with many prerequisites.

#### Approach 3: Bayesian Network Propagation

Model the concept graph as a Bayesian network where:
- Each concept node's mastery is a random variable
- Prerequisites are parent nodes
- The conditional probability `P(mastery_c | mastery_parents)` encodes how prerequisite mastery affects downstream mastery

When a prerequisite's mastery distribution changes (due to decay), belief propagation updates all descendant distributions.

**Strengths:** Theoretically principled. Handles uncertainty properly. Allows different concepts to be differentially sensitive to prerequisite decay.

**Weaknesses:** Computational cost of belief propagation on large graphs. Requires specifying conditional probability tables (or learning them from data).

#### Approach 4: Decay Cascade with Dampening

```
decay_penalty(c, p) = max(0, threshold - measured_mastery(p)) * edge_weight(p, c) * dampening^distance(p, c)
effective_mastery(c) = measured_mastery(c) - sum(decay_penalty(c, p) for p in all_ancestors(c))
```

Each decayed ancestor contributes a penalty that diminishes with graph distance.

**Strengths:** Intuitive. Long chains of decay have diminishing impact. Edge weights capture how critical each prerequisite is.

**Weaknesses:** Edge weights and dampening factor must be tuned. Risk of over- or under-penalizing depending on graph topology.

**Recommendation for Cena:** Start with Approach 2 (weighted prerequisite penalty) at launch. It is simple, graduated, and captures the essential dynamic. Evolve to Approach 3 (Bayesian network) when data supports learning the conditional probability tables. The Bayesian network approach naturally integrates with the MIRT migration, since MIRT's theta vector provides the ability estimates that can serve as evidence in the Bayesian network.

**Practical implication for spaced repetition:** When scheduling reviews, foundational concepts with many dependents should be prioritized even if their measured mastery is only slightly below threshold. The cost of letting a foundational concept decay is high because it effectively degrades the entire subtree.

**Priority scheduling formula:**
```
review_priority(c) = (threshold - p(recall_c)) * (1 + log(count(descendants(c))))
```

This weights review urgency by both the concept's own decay AND the number of downstream concepts it supports.

---

## 4. Grading in a Concept Graph

### 4.1 Concept-Level Mastery

The base unit. Each concept node in the graph has a mastery state per student.

**Recommended state representation:**

```
ConceptMasteryState {
  mastery_probability: Float      // 0.0-1.0, from BKT/MIRT
  half_life_hours: Float          // from HLR, for spaced repetition
  last_interaction: Timestamp     // for decay computation
  bloom_level: Int                // 0-6, highest demonstrated level
  confidence_self: Float          // 0.0-1.0, student self-assessment
  error_pattern: ErrorType[]      // recent error classifications
  attempt_count: Int              // total attempts
  current_streak: Int             // consecutive correct

  // Computed properties
  recall_probability: Float       // p(recall) = 2^(-delta/h), real-time
  effective_mastery: Float        // min(mastery, prerequisite_support)
}
```

**Mastery thresholds:**
```
Not Started:   mastery_probability < 0.10
Introduced:    0.10 <= mastery_probability < 0.40
Developing:    0.40 <= mastery_probability < 0.70
Proficient:    0.70 <= mastery_probability < 0.90
Mastered:      mastery_probability >= 0.90
```

These thresholds should be tunable per subject and validated against Bagrut exam performance.

---

### 4.2 Topic and Domain Aggregation

**Three-level hierarchy:**
```
Domain (e.g., "Mathematics")
  └── Topic (e.g., "Calculus")
       └── Concept (e.g., "Chain Rule")
```

#### Topic-Level Mastery

**Strategy 1: Weighted Average**
```
topic_mastery(T) = sum(w_c * effective_mastery(c) for c in T) / sum(w_c)
```

Weights can be:
- **Uniform:** All concepts equal
- **Importance-weighted:** Concepts with more dependents or higher exam frequency weighted more
- **Difficulty-weighted:** Harder concepts (lower average mastery across all students) weighted more, since mastering them is more meaningful

**Strategy 2: Minimum Prerequisite (Conservative)**
```
topic_mastery(T) = min(effective_mastery(c) for c in core_concepts(T))
```

Where `core_concepts(T)` are the essential concepts in the topic (not every peripheral concept).

**Strategy 3: Percentile-Based**
```
topic_mastery(T) = percentile_25(effective_mastery(c) for c in T)
```

The 25th percentile gives a robust estimate that is not dominated by outlier concepts (either very easy or very hard).

**Recommendation:** Use Strategy 1 (weighted average) for the student dashboard (feels fair, shows progress) and Strategy 2 (minimum prerequisite) for internal decisions (item selection, topic readiness — conservative is safer).

#### Domain-Level Mastery

```
domain_mastery(D) = weighted_mean(topic_mastery(T) for T in D)
```

Topic weights should reflect:
- Bagrut exam weight allocation (e.g., Calculus = 30% of 5-unit Math)
- Relative topic size (number of concepts)
- Student's declared exam target (3-unit vs. 5-unit)

---

### 4.3 Partial Mastery Propagation

**The problem:** A student can partially know a concept (0 < mastery < 1). How does partial mastery of a prerequisite affect the downstream concept?

**Soft gating model:**
```
readiness(c) = product(sigmoid(k * (mastery(p) - threshold)) for p in prerequisites(c))
```

Where `k` controls the steepness of the gate:
- `k = 1`: Very soft gate — partial mastery partially enables downstream concepts
- `k = 10`: Hard gate — mastery must be near threshold to enable downstream
- `k = infinity`: Binary gate — same as KST

**Fuzzy prerequisite satisfaction:**
```
fuzzy_PSI(c) = mean(min(mastery(p) / threshold, 1.0) for p in prerequisites(c))
```

This gives credit for partial prerequisite mastery (e.g., if threshold is 0.8 and prerequisite mastery is 0.6, the student gets 0.75 credit for that prerequisite).

**Practical use:** The readiness score determines:
1. Whether the concept appears in the learning frontier (readiness > 0.7)
2. The expected difficulty of items from this concept (lower readiness = higher effective difficulty)
3. The methodology choice (low readiness + hard concept = more scaffolding needed)

---

### 4.4 Confidence Intervals on Mastery Estimates

**Why this matters:** A mastery estimate of 0.75 based on 3 attempts is very different from 0.75 based on 30 attempts. The system should know the difference.

#### BKT Confidence

BKT's posterior `P(L_t)` is a point estimate. The uncertainty can be approximated by:

```
SE(P(L_t)) ≈ sqrt(P(L_t) * (1 - P(L_t)) / effective_sample_size)
```

Where `effective_sample_size` accounts for the information content of each observation (items with moderate difficulty are more informative than very easy or very hard items).

**95% confidence interval:** `P(L_t) +/- 1.96 * SE`

#### MIRT Confidence

MIRT naturally produces standard errors on the theta estimates:

```
SE(theta_k) = 1 / sqrt(I_k(theta))
```

Where `I_k(theta)` is the Fisher information for dimension k, which depends on:
- Number of items answered that load on dimension k
- How discriminating those items are (higher `a_j` = more information)
- How well-targeted those items are to the student's ability level

**Practical implication for Cena:**
- Display mastery as a range, not a point: "You're at 72-85% mastery of quadratic equations"
- Use the confidence width to drive item selection: concepts with wide confidence intervals need more items to narrow the estimate
- Do not trigger mastery events (`ConceptMastered`) until the lower bound of the confidence interval exceeds the threshold

#### Beta Distribution for Per-Concept Mastery

An elegant alternative to BKT for per-concept mastery:

```
mastery ~ Beta(alpha, beta)
  where alpha = 1 + correct_count, beta = 1 + incorrect_count
```

- Mean: `alpha / (alpha + beta)`
- Variance: `alpha * beta / ((alpha + beta)^2 * (alpha + beta + 1))`
- 95% credible interval: `Beta_quantile(0.025, alpha, beta)` to `Beta_quantile(0.975, alpha, beta)`

**Strengths:** Closed-form, fast, naturally incorporates sample size into confidence.

**Weaknesses:** Does not model learning (transition from unknown to known), temporal dynamics, or item difficulty.

**Use case:** As a lightweight confidence layer on top of BKT — the Beta distribution captures "how much data do we have" while BKT captures "what state is the student in."

---

### 4.5 Visualization of Mastery States

**How to present mastery in the knowledge graph visualization (Cena's "hero feature"):**

#### Node Coloring

```
Color mapping:
  Not Started  → Gray (#CBD5E1)
  Introduced   → Light blue (#93C5FD)
  Developing   → Yellow (#FDE047)
  Proficient   → Light green (#86EFAC)
  Mastered     → Green (#22C55E)
  Decaying     → Orange (#FB923C) — mastered but recall probability dropping
  Blocked      → Red outline (#EF4444) — prerequisites not met
```

#### Node Size

Scale node size by importance (PageRank in the graph or exam weight):
```
node_radius = base_radius * (1 + importance_score * scale_factor)
```

#### Edge Rendering

```
Prerequisite edge:
  Solid line if prerequisite is mastered
  Dashed line if prerequisite is developing
  Dotted line if prerequisite is not started

Edge color: gradient from source mastery color to target mastery color
Edge width: proportional to prerequisite strength (how critical the prerequisite is)
```

#### Animation

- **Mastery increase:** Node pulses briefly when mastery crosses a threshold
- **Decay warning:** Node slowly desaturates over time as recall probability drops
- **New concept unlocked:** Edges to the new concept animate from dashed to solid when prerequisites are met

#### Dashboard Metrics

For the student-facing dashboard:
```
[Progress Ring: 67% of Calculus concepts mastered]
[Heat Map: Time since last review per concept]
[Frontier Display: "3 new concepts ready to learn"]
[Decay Alert: "2 concepts need review this week"]
[Streak: "12-day streak, 47 concepts strengthened"]
```

For the teacher dashboard:
```
[Class Heat Map: Which concepts are weakest across the class]
[Distribution: Histogram of mastery levels per concept]
[Stagnation Alerts: Students who have plateaued]
[Prerequisite Gaps: Common foundational gaps in the class]
```

---

## 5. Composite Scoring

### 5.1 Multi-Signal Fusion Architecture

The goal is to combine all available signals into a single, defensible mastery score per concept that:
1. Reflects the best estimate of the student's current knowledge
2. Incorporates uncertainty
3. Is updatable in real-time
4. Is interpretable

**Available signals per concept:**

| Signal | Source | Update Frequency | Reliability |
|--------|--------|-----------------|-------------|
| BKT/MIRT mastery probability | Knowledge tracing | After each interaction | High |
| Recall probability (HLR) | Spaced repetition model | Continuous (time-based) | High |
| Accuracy (rolling) | Direct observation | After each interaction | Medium |
| Response latency trend | Direct observation | After each interaction | Medium |
| Error type distribution | LLM classification | After each interaction | Medium |
| Bloom's level demonstrated | Item tagging | After each interaction | Medium-High |
| Self-assessed confidence | Student input | After each concept review | Low (but informative) |
| Annotation depth | NLP analysis | When student annotates | Low frequency |
| Feynman explanation score | LLM grading | Infrequent (prompted) | High when available |
| Prerequisite support | Graph computation | After any prereq update | High |

---

### 5.2 Bayesian Composite Approach

**The principled way:** Treat each signal as noisy evidence about the true latent mastery state, and combine them via Bayesian updating.

**Formulation:**

Prior on mastery: `P(mastery)` from BKT/MIRT.

Update with each additional signal:
```
P(mastery | signal_k) ∝ P(signal_k | mastery) * P(mastery)
```

For example, if the HLR recall probability is 0.6 but the BKT mastery is 0.9, the Bayesian posterior should be somewhere between — probably closer to 0.6 because the student hasn't practiced recently.

**Practical simplification:** Represent mastery as a Beta distribution and update it with each signal:

```
Prior: Beta(alpha, beta) from BKT
After HLR signal: Beta(alpha * recall_weight, beta * (1 - recall_weight))
After accuracy signal: directly update alpha, beta with observed correct/incorrect counts
```

**Strengths:**
- Principled uncertainty propagation
- Each signal contributes proportionally to its reliability
- The posterior naturally reflects the combined state of all evidence

**Weaknesses:**
- Requires specifying the likelihood function for each signal (how does Bloom's level relate to mastery probability?)
- Heterogeneous signals (probability, ordinal, categorical) are hard to combine in a single Bayesian framework
- Computationally more complex than weighted averaging

---

### 5.3 Weighted Linear Combination

**The pragmatic way:** Combine signals via a weighted sum.

**Formulation:**
```
composite_mastery(c) = w_1 * bkt_mastery(c)
                     + w_2 * recall_probability(c)
                     + w_3 * rolling_accuracy(c)
                     + w_4 * normalized_bloom_level(c)
                     + w_5 * latency_score(c)
                     + w_6 * confidence_calibration_score(c)
```

Where all signals are normalized to [0, 1] and weights sum to 1.

**Suggested initial weights for Cena:**

| Signal | Weight | Rationale |
|--------|--------|-----------|
| BKT/MIRT mastery | 0.35 | Core statistical model, highest reliability |
| Recall probability (HLR) | 0.25 | Captures temporal decay, critical for concept graph |
| Rolling accuracy (last 10) | 0.15 | Direct evidence, complements the model |
| Bloom's level / max(6) | 0.10 | Depth of understanding signal |
| Latency score | 0.05 | Fluency indicator |
| Error type score | 0.05 | Misconception absence indicator |
| Self-assessment calibration | 0.05 | Metacognitive bonus/penalty |

**Prerequisite adjustment (applied after combination):**
```
final_mastery(c) = composite_mastery(c) * prerequisite_support(c)
```

**Strengths:**
- Simple, fast, interpretable
- Weights are tunable via A/B testing
- Graceful degradation: if a signal is missing (e.g., no Feynman score), its weight is redistributed to others

**Weaknesses:**
- Linear combination assumes independence between signals (which is violated — accuracy and BKT are correlated)
- Weights are somewhat arbitrary unless learned from data
- Does not naturally produce confidence intervals

---

### 5.4 Ensemble / Stacking Approach

**The data-driven way:** Train a meta-model that learns the optimal combination of signals.

**Formulation:**

Features: all available signals per concept (BKT mastery, recall probability, accuracy, latency, error types, Bloom's level, self-assessment, prerequisite support, attempt count, time since first exposure, ...).

Target variable: future performance on the concept (correct/incorrect on next item).

Model: Gradient boosted trees (XGBoost/LightGBM) or a small neural network.

```
composite_mastery(c) = meta_model(feature_vector(c))
```

**Strengths:**
- Learns non-linear interactions between signals
- Automatically discovers which signals matter most
- Can be validated against held-out future performance data
- Feature importance analysis reveals which signals drive mastery prediction

**Weaknesses:**
- Requires labeled training data (future performance as the target)
- Risk of overfitting on student-specific patterns
- Less interpretable than weighted averaging
- Cold-start: needs substantial data before the meta-model is useful

**When to use it:**
- At scale (50K+ students), as a research tool to optimize the weights in the linear combination
- NOT at launch — start with hand-tuned weights

---

### 5.5 Recommended Architecture for Cena

**Phase 1 (Launch — BKT era):**

```
mastery_score(c) = bkt_probability(c)     // primary signal
recall_score(c)  = 2^(-delta / h_c)       // HLR decay

displayed_mastery(c) = min(mastery_score(c), recall_score(c))
effective_mastery(c) = displayed_mastery(c) * prerequisite_support(c)

// Qualitative signals stored but NOT yet fused into mastery
// They drive methodology switching and stagnation detection
```

**Rationale:** At launch, data is sparse. BKT is the only well-calibrated signal. HLR handles temporal decay. Prerequisite support handles graph structure. Qualitative signals drive the pedagogy context (methodology switching, stagnation detection) but not the mastery number itself.

**Phase 2 (Scale — MIRT migration):**

```
mirt_theta(c)    = MIRT posterior for concept dimension
recall_score(c)  = 2^(-delta / h_c)
accuracy_10(c)   = rolling accuracy over last 10 attempts
bloom_level(c)   = highest demonstrated Bloom's level

composite_mastery(c) = 0.35 * mirt_theta_normalized(c)
                     + 0.25 * recall_score(c)
                     + 0.15 * accuracy_10(c)
                     + 0.10 * bloom_level(c) / 6
                     + 0.05 * latency_score(c)
                     + 0.05 * error_absence_score(c)
                     + 0.05 * calibration_score(c)

effective_mastery(c) = composite_mastery(c) * prerequisite_support(c)
confidence_interval  = MIRT standard error + sample size adjustment
```

**Phase 3 (Optimization — data-driven):**

Train a meta-model on Phase 2 data to optimize weights. Use the meta-model's feature importance to validate or adjust the hand-tuned weights. Run A/B tests: composite mastery score vs. simple BKT mastery, measured against long-term retention and Bagrut exam correlation.

**Cena implementation mapping across all phases:**
- The `StudentProfile` virtual actor (Proto.Actor, event-sourced on PostgreSQL/Marten) is the single source of truth for all mastery computation — it holds the full `ConceptMasteryState` per concept
- Phase 1 computation runs inline in the actor's `Handle(ConceptAttempted)` method — BKT update + HLR update + prerequisite propagation via Neo4j graph traversal (cached in-memory at actor startup)
- `ConceptMastered` events are emitted when `effective_mastery(c) >= 0.90` — consumed by Engagement Context (XP, badges, streak) and Analytics Context (dashboards)
- `MasteryDecayed` events are emitted by the actor's `ReceiveTimeout` handler when `recall_score(c)` drops below 0.70 — consumed by Outreach Context (push notifications, WhatsApp reminders)
- `StagnationDetected` events trigger when the composite stagnation score (accuracy plateau + latency drift + session abandonment + error repetition + annotation sentiment) exceeds 0.7 for 3 consecutive sessions — consumed by Pedagogy Context for `MethodologySwitched`
- Phase 2 MIRT theta vectors are computed offline (Python batch job on Analytics read replica) and published to the `StudentProfile` actor via NATS JetStream `learner.mirt.updated` — the actor integrates the theta vector as a prior for BKT
- The knowledge graph in Neo4j stores the prerequisite structure; the `StudentProfile` actor caches its local subgraph (concepts the student has encountered + their prerequisites) for fast `prerequisite_support(c)` computation without per-interaction Neo4j queries
- Phase 3 meta-model weights are stored as A/B experiment cohort configuration in the `StudentProfile` — each student's experiment cohort assignment determines which weight vector is used for composite scoring
- SignalR WebSocket push delivers real-time mastery updates to the React Native/React clients — when any concept's `effective_mastery` changes, the diff is pushed to the client's knowledge graph visualization

---

## Appendix: Quick Reference — Method Selection Guide

| Situation | Recommended Method | Why |
|-----------|-------------------|-----|
| **New student, no data** | KST diagnostic (10-15 questions) | Rapidly estimates knowledge state using prerequisite structure |
| **Early interactions (<20 per concept)** | BKT | Works with minimal data, interpretable |
| **Mature data (50+ interactions per concept)** | MIRT (topic-level) + BKT (concept-level) | Multi-dimensional, handles cross-concept dependencies |
| **Scheduling reviews** | HLR | Trainable half-life, proven at scale |
| **Detecting stagnation** | Rolling accuracy + latency trend + error classification | Direct behavioral signals, no model needed |
| **Methodology switching trigger** | Error taxonomy + stagnation composite score | Error type determines which methodology to switch to |
| **Prerequisite gap detection** | Prerequisite Satisfaction Index + effective mastery | Graph-based, identifies foundational weaknesses |
| **Item selection (next question)** | MIRT information gain or BKT weakest-concept | Choose the item that maximizes information about the most uncertain concept |
| **Dashboard display** | Composite score + Bloom's level + confidence interval | Combines statistical rigor with pedagogical meaningfulness |
| **Validating graph structure** | SPARFA or DKT attention analysis | Data-driven discovery of latent concept relationships |

---

## Appendix B: Neo4j Cypher Queries for Graph Operations

The following Cypher queries implement the key graph-based computations described in this document against Cena's Neo4j concept graph schema.

### B.1 Computing prerequisite_support(c)

Computes the minimum mastery of all direct prerequisites for a given concept and student. Returns the prerequisite floor that caps effective mastery.

```cypher
// prerequisite_support(c) = min(mastery(p) for p in prerequisites(c))
MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(p:Concept)-[:IS_PREREQUISITE_OF]->(c:Concept {id: $conceptId})
WITH c, collect(m.mastery_probability) AS prereq_masteries
RETURN c.id AS concept_id,
       CASE WHEN size(prereq_masteries) = 0 THEN 1.0
            ELSE reduce(min_val = 1.0, x IN prereq_masteries | CASE WHEN x < min_val THEN x ELSE min_val END)
       END AS prerequisite_support,
       size(prereq_masteries) AS prerequisite_count
```

### B.2 Finding the Learning Frontier

Identifies concepts whose prerequisites are all mastered (PSI = 1.0) but which the student has not yet mastered. These are the concepts the student is ready to learn next.

```cypher
// Learning frontier: concepts where all prereqs mastered but concept itself is not
MATCH (c:Concept)
WHERE c.subject = $subject
// Count total prerequisites
OPTIONAL MATCH (p:Concept)-[:IS_PREREQUISITE_OF]->(c)
WITH c, collect(p) AS all_prereqs
// Count mastered prerequisites for this student
OPTIONAL MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(p:Concept)-[:IS_PREREQUISITE_OF]->(c)
WHERE m.mastery_probability >= $masteryThreshold
WITH c, all_prereqs, collect(p) AS mastered_prereqs
WHERE size(all_prereqs) = size(mastered_prereqs)
// Exclude already-mastered concepts
OPTIONAL MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(c)
WHERE m.mastery_probability < $masteryThreshold OR m IS NULL
RETURN c.id AS concept_id,
       c.name AS concept_name,
       c.difficulty AS intrinsic_difficulty,
       size(all_prereqs) AS prerequisite_count
ORDER BY c.difficulty ASC
```

### B.3 Computing cluster_mastery

Computes the weighted average mastery for a topic cluster, using concept importance (PageRank or exam weight) as weights.

```cypher
// cluster_mastery(T) = weighted_mean(mastery(c) for c in T, weights = importance(c))
MATCH (c:Concept)-[:BELONGS_TO]->(t:TopicCluster {id: $clusterId})
OPTIONAL MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(c)
WITH t, c,
     COALESCE(m.mastery_probability, 0.0) AS mastery,
     COALESCE(c.importance_weight, 1.0) AS weight
WITH t,
     sum(mastery * weight) AS weighted_mastery_sum,
     sum(weight) AS total_weight,
     count(c) AS concept_count,
     collect({id: c.id, mastery: mastery, weight: weight}) AS concepts
RETURN t.id AS cluster_id,
       t.name AS cluster_name,
       weighted_mastery_sum / total_weight AS cluster_mastery,
       concept_count,
       concepts
```

### B.4 Finding Decay-Risk Concepts

Identifies concepts that the student has previously mastered but whose predicted recall probability has dropped below a threshold, ordered by number of downstream dependents (highest impact first).

```cypher
// Decay-risk: mastered concepts where p(recall) = 2^(-delta/h) < threshold
MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(c:Concept)
WHERE m.mastery_probability >= 0.90
  AND m.half_life_hours > 0
WITH c, m,
     duration.between(m.last_interaction, datetime()).hours AS hours_elapsed,
     m.half_life_hours AS half_life
WITH c, m, hours_elapsed, half_life,
     // p(recall) = 2^(-delta/h)
     2.0 ^ (-1.0 * hours_elapsed / half_life) AS recall_probability
WHERE recall_probability < $recallThreshold
// Count downstream dependents to prioritize foundational concepts
OPTIONAL MATCH (c)-[:IS_PREREQUISITE_OF*1..]->(downstream:Concept)
WITH c, m, recall_probability, hours_elapsed, half_life, count(DISTINCT downstream) AS dependent_count
RETURN c.id AS concept_id,
       c.name AS concept_name,
       m.mastery_probability AS mastery,
       recall_probability,
       hours_elapsed,
       half_life,
       dependent_count,
       // review_priority = (threshold - p(recall)) * (1 + log(dependents))
       ($recallThreshold - recall_probability) * (1 + log(dependent_count + 1.0)) AS review_priority
ORDER BY review_priority DESC
LIMIT 10
```

### B.5 Computing effective_mastery with Prerequisite Floor

Recursively computes effective mastery by propagating the prerequisite floor constraint through the graph. Uses the weighted prerequisite penalty approach (Approach 2 from Section 3.6).

```cypher
// effective_mastery(c) = measured_mastery(c) * product(max(mastery(p)/threshold, 1.0))
MATCH (s:Student {id: $studentId})-[m:HAS_MASTERY]->(c:Concept {id: $conceptId})
// Traverse all prerequisite ancestors (up to 5 levels deep to bound computation)
OPTIONAL MATCH path = (ancestor:Concept)-[:IS_PREREQUISITE_OF*1..5]->(c)
OPTIONAL MATCH (s)-[am:HAS_MASTERY]->(ancestor)
WITH c, m,
     collect(DISTINCT {
       ancestor_id: ancestor.id,
       ancestor_mastery: COALESCE(am.mastery_probability, 0.0),
       distance: length(path)
     }) AS ancestor_data
WITH c, m, ancestor_data,
     // Weighted prerequisite penalty with dampening by distance
     reduce(penalty = 1.0, a IN ancestor_data |
       penalty * CASE
         WHEN a.ancestor_mastery >= $masteryThreshold THEN 1.0
         ELSE (a.ancestor_mastery / $masteryThreshold) * (0.8 ^ (a.distance - 1))
       END
     ) AS prerequisite_penalty
RETURN c.id AS concept_id,
       m.mastery_probability AS measured_mastery,
       prerequisite_penalty,
       m.mastery_probability * prerequisite_penalty AS effective_mastery,
       m.half_life_hours,
       m.bloom_level,
       m.attempt_count
```

---

## Sources and Key References

**Knowledge Tracing:**
- Corbett & Anderson (1995). Knowledge tracing: Modeling the acquisition of procedural knowledge. *User Modeling and User-Adapted Interaction*, 4(4), 253-278. doi:10.1007/BF01099821 (Original BKT paper)
- Piech et al. (2015). Deep Knowledge Tracing. *NeurIPS 2015*. arXiv:1506.05908 (DKT)
- Zhang et al. (2017). Dynamic Key-Value Memory Networks for Knowledge Tracing. *WWW 2017*. doi:10.1145/3038912.3052580 (DKVMN)
- Ghosh et al. (2020). Context-Aware Attentive Knowledge Tracing. *KDD 2020*. doi:10.1145/3394486.3403282 (AKT)
- Lan et al. (2014). Sparse Factor Analysis for Learning and Content Analytics. *JMLR*, 15(1), 1771-1812. (SPARFA)
- Pavlik et al. (2009). Performance Factors Analysis — A New Alternative to Knowledge Tracing. *AIED 2009*. doi:10.3233/978-1-60750-028-5-531 (PFA)

**Item Response Theory:**
- Reckase (2009). *Multidimensional Item Response Theory*. Springer. doi:10.1007/978-0-387-89976-3 (MIRT comprehensive reference)
- De Ayala (2009). *The Theory and Practice of Item Response Theory*. Guilford Press.
- Rasch (1960). *Probabilistic Models for Some Intelligence and Attainment Tests*. Danish Institute for Educational Research. (Original Rasch model)

**Forgetting and Spaced Repetition:**
- Settles & Meeder (2016). A Trainable Spaced Repetition Model for Language Learning. *ACL 2016*. doi:10.18653/v1/P16-1174 (HLR — Duolingo)
- Bjork & Bjork (1992). A New Theory of Disuse and an Old Theory of Stimulus Fluctuation. In *From Learning Processes to Cognitive Processes: Essays in Honor of William K. Estes*, Vol. 2, pp. 35-67. (Storage vs. retrieval strength)
- Lindsey et al. (2014). Improving Students' Long-Term Knowledge Retention Through Personalized Review. *Psychological Science*, 25(3), 639-647. doi:10.1177/0956797613504302 (DASH)
- Ebbinghaus (1885/1913). *Memory: A Contribution to Experimental Psychology*. Translated by Ruger & Bussenius. (Original forgetting curve)
- Leitner (1972). *Lernen lernen* (Learning to Learn). Herder. (Leitner spaced repetition box system)

**Knowledge Space Theory:**
- Doignon & Falmagne (2011). *Learning Spaces: Interdisciplinary Applied Mathematics*. Springer. doi:10.1007/978-3-642-01039-2

**Misconceptions:**
- Chi (2005). Commonsense Conceptions of Emergent Processes: Why Some Misconceptions Are Robust. *Journal of the Learning Sciences*, 14(2), 161-199. doi:10.1207/s15327809jls1402_1
- Vosniadou (2013). *International Handbook of Research on Conceptual Change*, 2nd ed. Routledge. doi:10.4324/9780203154472

**Bloom's Taxonomy:**
- Anderson & Krathwohl (2001). *A Taxonomy for Learning, Teaching, and Assessing: A Revision of Bloom's Taxonomy of Educational Objectives*. Longman.
- Bloom et al. (1956). *Taxonomy of Educational Objectives: The Classification of Educational Goals. Handbook I: Cognitive Domain*. David McKay Company. (Original Bloom's taxonomy)

**Cognitive Load and Learning Theory:**
- Sweller (1988). Cognitive Load During Problem Solving: Effects on Learning. *Cognitive Science*, 12(2), 257-285. doi:10.1207/s15516709cog1202_4 (Cognitive Load Theory — CLT)
- Vygotsky (1978). *Mind in Society: The Development of Higher Psychological Processes*. Harvard University Press. (Zone of Proximal Development — ZPD)
- Roediger & Karpicke (2006). Test-Enhanced Learning: Taking Memory Tests Improves Long-Term Retention. *Psychological Science*, 17(3), 249-255. doi:10.1111/j.1467-9280.2006.01693.x (Testing effect / retrieval practice)
- Kornell & Bjork (2008). Learning Concepts and Categories: Is Spacing the "Enemy of Induction"? *Psychological Science*, 19(6), 585-592. doi:10.1111/j.1467-9280.2008.02127.x (Interleaving effect)
- Bjork (1994). Memory and Metamemory Considerations in the Training of Human Beings. In *Metacognition: Knowing about Knowing*, pp. 185-205. MIT Press. (Desirable difficulties framework)
- Renkl & Atkinson (2003). Structuring the Transition from Example Study to Problem Solving in Cognitive Skill Acquisition: A Cognitive Load Perspective. *Educational Psychologist*, 38(1), 15-22. doi:10.1207/S15326985EP3801_3 (Worked examples with fading)

**Elo-Based Models:**
- Elo (1978). *The Rating of Chessplayers, Past and Present*. Arco. (Original Elo rating system)
- Pelánek (2016). Applications of the Elo Rating System in Adaptive Educational Systems. *Computers & Education*, 98, 169-179. doi:10.1016/j.compedu.2016.03.017 (Elo in education)

**Modern Spaced Repetition Algorithms:**
- Wozniak (1990). *SuperMemo 2 Algorithm*. SuperMemo World. (SM-2 algorithm — basis for Anki)
- Ye (2022). *Free Spaced Repetition Scheduler* (FSRS). Open-source. arXiv:2402.01032 (FSRS-4.5 — next-gen scheduler, outperforms SM-2 on 10K+ user study)

**Production Systems:**
- Duolingo Engineering Blog: Birdbrain, Session Generator, HLR papers
- OATutor (CAHLR, UC Berkeley): Open-source ITS with BKT implementation
- pyBKT: Python library for BKT parameter estimation (doi:10.21105/joss.05386)
- Squirrel AI: PKS model and MCM graph architecture

**Scaffolding and Transfer:**
- Wood, Bruner & Ross (1976). The Role of Tutoring in Problem Solving. *Journal of Child Psychology and Psychiatry*, 17(2), 89-100. doi:10.1111/j.1469-7610.1976.tb00381.x (Original scaffolding paper)
- Barnett & Ceci (2002). When and Where Do We Apply What We Learn? A Taxonomy for Far Transfer. *Psychological Bulletin*, 128(4), 612-637. doi:10.1037/0033-2909.128.4.612 (Near transfer vs. far transfer taxonomy)
