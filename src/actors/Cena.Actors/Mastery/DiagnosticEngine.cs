// =============================================================================
// Cena Platform -- Diagnostic Engine
// MST-013: KST adaptive onboarding diagnostic
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>
/// Adaptive diagnostic engine using Knowledge Space Theory.
/// Selects maximally informative questions and maintains a posterior
/// distribution over feasible knowledge states.
/// </summary>
public static class DiagnosticEngine
{
    /// <summary>
    /// Select the next concept to test: the one whose inclusion probability
    /// is closest to 0.5 (maximum information / entropy reduction).
    /// </summary>
    public static string SelectNextConcept(
        IReadOnlyList<KnowledgeState> feasibleStates,
        float[] posterior)
    {
        // Collect all concepts across all states
        var conceptScores = new Dictionary<string, float>();

        for (int i = 0; i < feasibleStates.Count; i++)
        {
            if (posterior[i] <= 0f) continue;
            foreach (var concept in feasibleStates[i].MasteredConcepts)
            {
                if (!conceptScores.ContainsKey(concept))
                    conceptScores[concept] = 0f;
                conceptScores[concept] += posterior[i];
            }
        }

        // Pick concept with P closest to 0.5 (maximally informative)
        string? bestConcept = null;
        float bestDistance = float.MaxValue;

        foreach (var (concept, prob) in conceptScores)
        {
            float distance = MathF.Abs(prob - 0.5f);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestConcept = concept;
            }
        }

        return bestConcept ?? feasibleStates[0].MasteredConcepts.FirstOrDefault() ?? "";
    }

    /// <summary>
    /// Update posterior after observing a response.
    /// Correct: states containing concept get weight * 0.9, others * 0.1.
    /// Incorrect: states containing concept get weight * 0.1, others * 0.9.
    /// Skip: weak signal (0.3 / 0.7).
    /// </summary>
    public static float[] UpdatePosterior(
        float[] prior,
        IReadOnlyList<KnowledgeState> states,
        string testedConceptId,
        bool isCorrect,
        bool isSkip = false)
    {
        float pContains, pNotContains;
        if (isSkip)
        {
            pContains = 0.3f;
            pNotContains = 0.7f;
        }
        else if (isCorrect)
        {
            pContains = 0.9f;
            pNotContains = 0.1f;
        }
        else
        {
            pContains = 0.1f;
            pNotContains = 0.9f;
        }

        var posterior = new float[prior.Length];
        float sum = 0f;

        for (int i = 0; i < states.Count; i++)
        {
            float likelihood = states[i].Contains(testedConceptId) ? pContains : pNotContains;
            posterior[i] = prior[i] * likelihood;
            sum += posterior[i];
        }

        // Normalize
        if (sum > 0f)
        {
            for (int i = 0; i < posterior.Length; i++)
                posterior[i] /= sum;
        }

        return posterior;
    }

    /// <summary>
    /// Run the full adaptive diagnostic. Asks 10-15 questions, updating the
    /// posterior after each. Returns the MAP estimate as the diagnostic result.
    /// </summary>
    public static DiagnosticResult RunDiagnostic(
        IConceptGraphCache graphCache,
        Func<string, bool?> askQuestion,
        int minQuestions = 10,
        int maxQuestions = 15)
    {
        var states = KnowledgeStateSpace.BuildFeasibleStates(graphCache);
        var posterior = new float[states.Count];
        Array.Fill(posterior, 1.0f / states.Count); // uniform prior

        var askedConcepts = new HashSet<string>();
        int questionsAsked = 0;

        for (int q = 0; q < maxQuestions; q++)
        {
            // Select most informative concept not yet asked
            string concept = SelectNextUnaskedConcept(states, posterior, askedConcepts);
            if (string.IsNullOrEmpty(concept)) break;

            askedConcepts.Add(concept);
            questionsAsked++;

            // Ask the question
            bool? answer = askQuestion(concept);

            // Update posterior
            if (answer == null)
                posterior = UpdatePosterior(posterior, states, concept, false, isSkip: true);
            else
                posterior = UpdatePosterior(posterior, states, concept, answer.Value);

            // Early stop: if posterior is concentrated enough after min questions
            if (q >= minQuestions - 1)
            {
                float maxProb = posterior.Max();
                if (maxProb > 0.80f)
                    break; // high confidence
            }
        }

        // MAP estimate: state with highest posterior
        int mapIndex = Array.IndexOf(posterior, posterior.Max());
        var mapState = states[mapIndex];

        // Gap concepts: all concepts NOT in the MAP state
        var allConcepts = graphCache.Concepts.Keys.ToImmutableHashSet();
        var gaps = allConcepts.Except(mapState.MasteredConcepts);

        return new DiagnosticResult(
            MasteredConcepts: mapState.MasteredConcepts,
            GapConcepts: gaps,
            Confidence: posterior[mapIndex],
            QuestionsAsked: questionsAsked);
    }

    private static string SelectNextUnaskedConcept(
        IReadOnlyList<KnowledgeState> states,
        float[] posterior,
        HashSet<string> asked)
    {
        var conceptScores = new Dictionary<string, float>();

        for (int i = 0; i < states.Count; i++)
        {
            if (posterior[i] <= 0f) continue;
            foreach (var concept in states[i].MasteredConcepts)
            {
                if (asked.Contains(concept)) continue;
                if (!conceptScores.ContainsKey(concept))
                    conceptScores[concept] = 0f;
                conceptScores[concept] += posterior[i];
            }
        }

        string? best = null;
        float bestDist = float.MaxValue;

        foreach (var (concept, prob) in conceptScores)
        {
            float dist = MathF.Abs(prob - 0.5f);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = concept;
            }
        }

        return best ?? "";
    }
}
