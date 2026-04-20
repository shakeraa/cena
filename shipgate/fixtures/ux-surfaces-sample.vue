<!--
  Ship-gate positive-test fixture: UX-surface rules (prr-211)

  Purpose: This file deliberately contains every pattern in
  scripts/shipgate/shipgate-ux-surfaces.yml. The CI test suite
  (tests/shipgate/ux-surfaces.spec.mjs) runs ux-surface-scan.mjs against this
  fixture and asserts that EVERY rule fires at least once.

  Do NOT clean this file. Each line below is a trap. The file is whitelisted
  (excluded from non-fixture runs) by the scanner's EXCLUDE_REGEXES.
-->
<script setup lang="ts">
// Fixture imports (inert — fixture file is never wired into the app).
import { ref } from 'vue'

// The fixture registers EVERY banned surface name as if it were a template
// root so the scanner classifies the file as representing all five surfaces.
// <HintLadder /> <StepSolverCard /> <Sidekick /> <MathInput /> <FreeBodyDiagramConstruct />
</script>

<template>
  <!--
    ==========================================================================
    HintLadder surface — string bans, dom-coupled-class bans, aria checks,
    and emoji-in-rung check.
    ==========================================================================
  -->
  <!-- hintladder-aria-live: region with no screenreader markers. -->
  <div class="hint-ladder" data-testid="hint-ladder">
    <!-- hintladder-rung-warning-class: danger class on a rung element -->
    <VAlert
      class="hint-ladder-rung text-danger bg-warning"
      data-testid="hint-rung-1"
    >
      <!-- hintladder-penalty -->
      There is a penalty for asking.
      <!-- hintladder-lost-points -->
      You have lost points by asking this hint.
      <!-- hintladder-wrong-exclaim -->
      Wrong! Try again.
      <!-- hintladder-hint-n-of-m -->
      Hint 1 of 5 unlocked.
      <!-- hintladder-remaining-counter -->
      (2 remaining)
      <!-- hintladder-reveal-xp -->
      You used reveal XP to show this.
      <!-- hintladder-l1-emoji: emoji inside L1 rung -->
      Here is a hint 🎯 for you.
    </VAlert>

    <!-- hintladder-rung-warning-color-prop: color="warning" on a rung VAlert -->
    <VAlert
      class="hint-ladder__rung"
      color="warning"
      type="error"
      data-testid="hint-rung-2"
    >
      Second rung copy.
    </VAlert>
  </div>

  <!--
    ==========================================================================
    StepSolverCard surface
    ==========================================================================
  -->
  <div class="step-solver-card">
    <!-- stepsolver-countdown -->
    <p>Time is running out — finish the step now.</p>
    <!-- stepsolver-percentile-compare -->
    <p>You are in the 42nd percentile compared to peers.</p>
    <!-- stepsolver-wrong-exclaim -->
    <p>Incorrect! That step is invalid.</p>
    <!-- stepsolver-penalty -->
    <p>Penalty applied to your step score.</p>
    <!-- stepsolver-time-pressure-class -->
    <div class="step-status text-danger animate-pulse">
      Step pending...
    </div>
  </div>

  <!--
    ==========================================================================
    Sidekick surface (file does not exist in repo yet — fixture still fires)
    ==========================================================================
  -->
  <div class="sidekick">
    <!-- sidekick-streak -->
    <p>Your streak is 5 days.</p>
    <!-- sidekick-daily-quota -->
    <p>Daily quota remaining: 3 questions.</p>
    <!-- sidekick-catch-up -->
    <p>You need to catch up — you're falling behind.</p>
    <!-- sidekick-loss-aversion -->
    <p>Don't lose your progress; don't miss out today.</p>
  </div>

  <!--
    ==========================================================================
    MathInput surface
    ==========================================================================
  -->
  <div class="math-input">
    <!-- mathinput-wrong-exclaim -->
    <p>Wrong! Re-enter the expression.</p>
    <!-- mathinput-invalid-shame -->
    <p>That's not right. Try harder.</p>
  </div>

  <!--
    ==========================================================================
    FreeBodyDiagramConstruct surface
    ==========================================================================
  -->
  <div class="fbd-construct">
    <!-- fbd-chance-to-score -->
    <p>This is your chance to score more marks.</p>
    <!-- fbd-variable-ratio -->
    <p>Lucky bonus round unlocked — jackpot!</p>
    <!-- fbd-penalty -->
    <p>Penalty deduction for wrong vector direction.</p>
  </div>
</template>
