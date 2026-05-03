# Hostile SymPy Templates (prr-010)

Canary fixtures exercised by `SymPySandbox.CanarySuiteTests`. Each file
contains one attack payload against the SymPy sidecar pipeline. The tests
assert that every payload is contained (either rejected pre-parse by the
`SymPyTemplateGuard`, or rejected by the Python sidecar's own whitelist)
within a bounded time budget.

## Payloads

| File | Attack class | Expected containment |
|---|---|---|
| `memory-bomb.txt` | Memory exhaustion via nested huge-power chain | Length cap or parser rejection — no host OOM |
| `infinite-recursion.txt` | Stack exhaustion via recursive functional equation | Timeout / parser rejection |
| `subclasses-escape.txt` | Python dunder-chain sandbox escape | Token ban (`__`) rejects before parse |
| `import-escape.txt` | Injected `__import__('os').system(...)` | Token ban (`__`, `import`) rejects before parse |
| `printing-ssrf.txt` | SSRF via `sympy.printing.preview` (LaTeX-to-PNG) | Token ban (`printing.preview`, `preview(`) rejects before parse |

## Ground rules

- One payload per file. Plain UTF-8 text, no surrounding whitespace.
- Keep payloads minimal — do NOT add real-world command strings that could be
  mistaken for an active exploit in logs or search indexes.
- When adding a new payload, update the table above, add a matching
  `[InlineData]` entry in `SymPySandbox.CanarySuiteTests`, and describe the
  attack class + expected containment path in the `[Theory]` summary comment.
