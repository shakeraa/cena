# Tight-Match Weak-Dedup Re-Check

Re-verification of 14 weak-dedup entries. Rule: a tension is 'real' only if both opposing labels actually appear on at least one finding in the source set. 'misfire' = the heuristic paired unrelated labels.
Summary: 10/14 weak-dedups show both tension-sides on the source findings (real tension); the rest are heuristic misfires.


| W-ID | prr-target | Tension | Labels present on findings | Real tension? | Verdict |
|---|---|---|---|---|---|
| W-001 | prr-002 | `cache` vs `no-cache` | cache, refactor-first, ship-now | no | heuristic misfire — keep merged |
| W-002 | prr-004 | `ship-now` vs `refactor-first` | refactor-first, ship-now, teacher-moderate | YES | real-tension — split task |
| W-003 | prr-006 | `parent-visible` vs `student-autonomy` | ship-now | no | heuristic misfire — keep merged |
| W-004 | prr-008 | `ship-now` vs `refactor-first` | cache, cas-gate, refactor-first, ship-now | YES | real-tension — split task |
| W-005 | prr-009 | `cache` vs `no-cache` | cache, cost-reduce, crypto-shred, hard-delete, no-cache, parent-visible, quality-first, ship-now, student-autonomy | YES | real-tension — split task |
| W-006 | prr-014 | `cache` vs `no-cache` | cache, cost-reduce, crypto-shred, hard-delete, no-cache, parent-visible, quality-first, ship-now, student-autonomy | YES | real-tension — split task |
| W-007 | prr-015 | `cache` vs `no-cache` | cache, no-cache, ship-now | YES | real-tension — split task |
| W-008 | prr-017 | `cache` vs `no-cache` | cache, no-cache, ship-now | YES | real-tension — split task |
| W-009 | prr-026 | `cache` vs `no-cache` | cas-gate, no-cache, ship-now, student-autonomy | no | heuristic misfire — keep merged |
| W-010 | prr-034 | `cas-gate` vs `teacher-moderate` | cas-gate, ship-now, teacher-moderate | YES | real-tension — split task |
| W-011 | prr-037 | `cache` vs `no-cache` | cache, cas-gate, no-cache, student-autonomy | YES | real-tension — split task |
| W-012 | prr-040 | `cas-gate` vs `teacher-moderate` | ship-now, student-autonomy, teacher-moderate | no | heuristic misfire — keep merged |
| W-013 | prr-149 | `ship-now` vs `refactor-first` | cache, cas-gate, cost-reduce, quality-first, refactor-first, ship-now | YES | real-tension — split task |
| W-014 | prr-150 | `ship-now` vs `refactor-first` | cas-gate, refactor-first, ship-now | YES | real-tension — split task |
