# LLM-010: End-to-End Socratic Dialogue — Multi-Turn, Caching

**Priority:** P1 — blocks core tutoring experience
**Blocked by:** LLM-005 (Prompts), ACT-001 (Cluster)
**Estimated effort:** 2 days
**Contract:** `contracts/llm/acl-interfaces.py` (SocraticQuestionRequest/Response, LLMGateway)

---

## Context

The Socratic dialogue is the primary tutoring interaction. Claude Sonnet 4.6 generates guided questions based on student mastery, dialogue history, and hint level. Prompt caching targets 60%+ hit rate for system prompt + student context.

## Subtasks

### LLM-010.1: Multi-Turn Dialogue Management
- [ ] Dialogue history maintained in LearningSessionActor state
- [ ] Max 50 turns per session (truncated FIFO)
- [ ] Context window management: system prompt + glossary + history + current question < 8K tokens
- [ ] Turn alternation: tutor, student, tutor, student (soft constraint, consecutive same-role allowed)

### LLM-010.2: Prompt Caching Strategy
- [ ] System prompt (role + glossary + rubric) identical across students -> cached by Anthropic
- [ ] Student context (mastery map, methodology) changes per student -> partial cache
- [ ] Target: 60%+ cache hit rate (measured via `cached` field in response)
- [ ] Cache-friendly ordering: static content first, dynamic content last

### LLM-010.3: End-to-End Flow Test
- [ ] Student starts session -> first Socratic question generated
- [ ] Student answers -> answer evaluated -> next question adapts
- [ ] Student requests hint -> hint_level incremented -> more scaffolded question
- [ ] Student masters concept -> session moves to next concept
- [ ] Dialogue coherent across 10+ turns

**Test:**
```python
async def test_socratic_multi_turn():
    gateway = LLMGateway()
    dialogue = []
    for turn in range(5):
        response = await gateway.socratic_question(SocraticQuestionRequest(
            student=test_student, concept=test_concept,
            dialogue_history=dialogue, current_mastery=0.3 + turn * 0.1,
            hint_level=min(turn, 3),
        ))
        assert response.question_he
        dialogue.append(DialogueTurn(role="tutor", content=response.question_he))
        dialogue.append(DialogueTurn(role="student", content="some answer"))

    assert len(dialogue) == 10
    assert response.model_used == ModelTier.SONNET
```

---

## Definition of Done
- [ ] Multi-turn Socratic dialogue working end-to-end
- [ ] Prompt caching achieving > 50% hit rate
- [ ] Dialogue coherent across 10+ turns
- [ ] PR reviewed by architect
