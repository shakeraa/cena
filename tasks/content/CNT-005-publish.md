# CNT-005: Publication Pipeline — Versioning, S3 Artifact, Neo4j Update, NATS Event, Hot-Reload

**Priority:** P1 — blocks content updates
**Blocked by:** CNT-004 (QA Pass), INF-005 (S3)
**Estimated effort:** 2 days
**Contract:** `contracts/backend/nats-subjects.md` (cena.curriculum.events.GraphPublished)

---

## Context

After QA passes, approved content is published as an immutable versioned artifact to S3, the Neo4j graph is updated atomically, a NATS event notifies all caches, and active actors hot-reload the new graph without disrupting sessions.

## Subtasks

### CNT-005.1: Versioned Artifact Creation

**Files to create/modify:**
- `scripts/content/publish_pipeline.py` — orchestrator
- `src/Cena.Data/CurriculumGraph/ArtifactBuilder.cs`

**Acceptance:**
- [ ] Artifact: `curriculum-math-v{semver}.tar.gz` containing `concepts.json`, `prerequisites.csv`, `questions.json`
- [ ] Uploaded to S3: `s3://cena-content/curriculum/math/v{version}/`
- [ ] Immutable: published versions never overwritten
- [ ] Manifest file: `manifest.json` with content hash, concept count, question count, publish timestamp

**Test:**
```python
def test_artifact_immutable():
    publish("v1.0.0")
    with pytest.raises(ArtifactExistsError):
        publish("v1.0.0")  # Same version blocked
```

---

### CNT-005.2: Neo4j Atomic Update

**Files to create/modify:**
- `scripts/content/neo4j_updater.py`

**Acceptance:**
- [ ] Neo4j updated via transaction: all-or-nothing
- [ ] Old concepts not in new version: soft-deleted (marked `deprecated: true`), not hard-deleted
- [ ] New concepts added, updated concepts modified
- [ ] Prerequisite edges updated atomically with concepts

**Test:**
```python
def test_atomic_update_rollback_on_failure():
    inject_neo4j_failure()
    with pytest.raises(PublishError):
        update_neo4j("v1.1.0")
    assert get_current_version() == "v1.0.0"  # Unchanged
```

---

### CNT-005.3: NATS Event + Hot-Reload

**Files to create/modify:**
- `src/Cena.Data/CurriculumGraph/GraphChangePublisher.cs`
- `src/Cena.Actors/CurriculumGraph/GraphCacheActor.cs`

**Acceptance:**
- [ ] `cena.curriculum.events.GraphPublished` event with `{ version, conceptCount, edgeCount, timestamp }`
- [ ] Redis KG cache invalidated on event
- [ ] In-memory graph actor reloads from Neo4j within 30 seconds
- [ ] Active sessions unaffected (use cached version until session ends)

**Test:**
```csharp
[Fact]
public async Task GraphPublished_TriggersHotReload()
{
    await PublishNewVersion("v1.1.0");
    await Task.Delay(TimeSpan.FromSeconds(5));
    var graphVersion = await _graphCache.GetCurrentVersion();
    Assert.Equal("v1.1.0", graphVersion);
}
```

---

## Rollback Criteria
- Re-publish previous version artifact to rollback

## Definition of Done
- [ ] Publication pipeline end-to-end: QA -> S3 -> Neo4j -> NATS -> hot-reload
- [ ] Immutable versioning prevents accidental overwrites
- [ ] PR reviewed by architect
