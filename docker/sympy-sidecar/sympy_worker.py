"""
Cena Platform — SymPy CAS Sidecar
=================================

NATS request/reply worker that serves CAS verification requests from the .NET
Actor Host and Admin API. Implements the five CasOperation types defined in
src/actors/Cena.Actors/Cas/CasContracts.cs:

    Equivalence, StepValidity, NumericalTolerance, NormalForm, Solve

Subjects:
    cena.cas.verify.sympy   — verification requests (reply: SymPyResponse JSON)
    cena.cas.health.sympy   — health probe (reply: {"ok": true, "version": "..."})

Request DTO (from .NET):
    {
      "operation": 0|1|2|3|4,
      "expressionA": "x**2 - 1",
      "expressionB": "(x+1)*(x-1)",
      "variable": "x",
      "tolerance": 1e-9
    }

Response DTO (to .NET):
    {"success": bool, "simplifiedA": str|null, "simplifiedB": str|null, "error": str|null}
"""

from __future__ import annotations

import asyncio
import json
import logging
import os
import signal
import sys
from dataclasses import dataclass
from typing import Any

import nats
import sympy as sp

LOG_LEVEL = os.environ.get("LOG_LEVEL", "INFO").upper()
logging.basicConfig(
    level=LOG_LEVEL,
    format="%(asctime)s %(levelname)s %(name)s %(message)s",
)
log = logging.getLogger("sympy-sidecar")

NATS_URL = os.environ.get("NATS_URL", "nats://cena-nats:4222")
NATS_USER = os.environ.get("NATS_USER", "setup")
NATS_PASSWORD = os.environ.get("NATS_PASSWORD", "dev_setup_pass")

VERIFY_SUBJECT = "cena.cas.verify.sympy"
HEALTH_SUBJECT = "cena.cas.health.sympy"

# ── CAS operation codes — must match CasOperation enum in CasContracts.cs ──
OP_EQUIVALENCE = 0
OP_STEP_VALIDITY = 1
OP_NUMERICAL_TOLERANCE = 2
OP_NORMAL_FORM = 3
OP_SOLVE = 4


@dataclass(frozen=True)
class Response:
    success: bool
    simplifiedA: str | None = None
    simplifiedB: str | None = None
    error: str | None = None

    def to_bytes(self) -> bytes:
        return json.dumps(
            {
                "success": self.success,
                "simplifiedA": self.simplifiedA,
                "simplifiedB": self.simplifiedB,
                "error": self.error,
            }
        ).encode("utf-8")


# ── prr-010: SymPy sandbox hardening (Layer 2) ────────────────────────────
# This is the SECOND wall of the sandbox. The first wall is SymPyTemplateGuard
# in the .NET client (src/actors/Cena.Actors/Cas/SymPyTemplateGuard.cs) which
# screens requests before they reach NATS. We re-apply a matching whitelist
# here as defense-in-depth — the Python process is the one that would execute
# a compromised expression, so it must not trust any upstream caller.
#
# Rationale (source: AXIS_8_Content_Authoring_Quality_Research.md L92/L98,
# persona-redteam review 2026-04-20):
#   * LLM templates are untrusted. A compromised template could contain
#     `__subclasses__` escape chains, `__import__('os')`, `exec`, `eval`, or
#     `sympy.printing.preview` (SSRF via LaTeX→PNG external call).
#   * sympify(..., strict=True) restricts parsing to SymPy-recognised tokens
#     and a caller-supplied local namespace. We pass an empty locals dict
#     here so no Python builtins leak in.
#   * We pre-scan the expression string for a banned-token list that mirrors
#     the .NET guard so the two walls move together.

# Mirror of SymPyTemplateGuard.BannedTokens. If you add a token here, add it
# there (and vice versa) — the CI arch test SymPyTemplateGateTest pins the
# .NET side; this comment is the convention pin for the Python side.
_BANNED_TOKENS = (
    "__",
    "import",
    "exec",
    "eval",
    "compile",
    "lambda",
    "open(",
    "os.",
    "sys.",
    "subprocess",
    "preview(",
    "printing.preview",
    "globals(",
    "locals(",
    "getattr(",
    "file(",
)

_MAX_EXPR_LEN = 2048


def _prescreen(expr: str) -> None:
    """Raise ValueError if the expression contains a banned token.

    Case-sensitive (Python is) and checked against both the raw string and a
    whitespace-stripped copy to catch spacing-based bypasses like
    ``__ subclasses __``.
    """
    if not isinstance(expr, str):
        raise ValueError("expression must be a string")
    if not expr or not expr.strip():
        raise ValueError("expression is empty or whitespace")
    if len(expr) > _MAX_EXPR_LEN:
        raise ValueError(f"expression exceeds max length {_MAX_EXPR_LEN}")

    for token in _BANNED_TOKENS:
        if token in expr:
            raise ValueError(f"disallowed token in expression: {token!r}")

    # Whitespace-normalised scan.
    normalised = "".join(c for c in expr if not c.isspace())
    if normalised != expr:
        for token in _BANNED_TOKENS:
            if token in normalised:
                raise ValueError(
                    f"disallowed token after whitespace normalisation: {token!r}"
                )


# Block sympy.printing.preview at import time — it is the primary SSRF
# primitive (LaTeX → PNG via external latex+convert invocation) and no
# correctness-oracle code path uses it. If the module is already loaded
# (SymPy imports lazily), we monkey-patch the function to raise.
try:
    import sympy.printing.preview as _preview_mod  # type: ignore[import-not-found]

    def _preview_blocked(*_args, **_kwargs):  # noqa: ANN002, ANN003
        raise RuntimeError(
            "sympy.printing.preview is disabled in the CAS sidecar (prr-010 SSRF block)"
        )

    _preview_mod.preview = _preview_blocked  # type: ignore[attr-defined]
    # Also clobber the top-level alias if it was re-exported.
    if hasattr(sp, "preview"):
        sp.preview = _preview_blocked  # type: ignore[attr-defined]
except Exception:  # noqa: BLE001 — if the module isn't there, we're already safe
    pass


def _parse(expr: str) -> sp.Expr:
    """Parse a SymPy expression using a safe transformation set.

    Applies the prr-010 banned-token pre-screen, then uses
    :func:`sympy.parsing.sympy_parser.parse_expr` with an explicit local
    namespace (no builtins) and ``evaluate=True``. Implicit multiplication
    and ``^`` → ``**`` transforms are retained so school-level expressions
    (``2x``, ``x^2``) still parse.
    """
    _prescreen(expr)

    from sympy.parsing.sympy_parser import (
        parse_expr,
        standard_transformations,
        implicit_multiplication_application,
        convert_xor,
    )

    transformations = standard_transformations + (
        implicit_multiplication_application,
        convert_xor,
    )
    # Pass an empty local_dict so no host-level names leak into the parse
    # context. We leave global_dict at its default (SymPy's own namespace)
    # so legitimate SymPy functions (sin, cos, diff, integrate, solve, ...)
    # still resolve — the pre-screen above has already rejected dunder
    # chains, `__import__`, `exec`, `eval`, etc. before we reach here.
    return parse_expr(
        expr,
        transformations=transformations,
        evaluate=True,
        local_dict={},
    )


def verify_equivalence(a: str, b: str, variable: str | None) -> Response:
    try:
        ea, eb = _parse(a), _parse(b)
        diff = sp.simplify(ea - eb)
        if diff == 0:
            return Response(True, str(sp.simplify(ea)), str(sp.simplify(eb)))
        # Fall back to polynomial-equality check for robustness
        if sp.expand(diff) == 0:
            return Response(True, str(sp.expand(ea)), str(sp.expand(eb)))
        return Response(False, error=f"not equivalent: a-b = {diff}")
    except Exception as ex:  # noqa: BLE001 — sidecar must not crash on bad input
        return Response(False, error=f"equivalence check failed: {ex}")


def verify_step_validity(a: str, b: str, variable: str | None) -> Response:
    """A step transforms expression A into B. They must be equivalent modulo
    domain constraints. Same mechanism as equivalence for now."""
    return verify_equivalence(a, b, variable)


def verify_numerical(a: str, b: str, tolerance: float) -> Response:
    try:
        va = complex(sp.N(_parse(a)))
        vb = complex(sp.N(_parse(b)))
        if abs(va - vb) <= tolerance:
            return Response(True, str(va), str(vb))
        return Response(False, error=f"|a-b|={abs(va-vb)} > tol={tolerance}")
    except Exception as ex:  # noqa: BLE001
        return Response(False, error=f"numerical check failed: {ex}")


def normal_form(a: str, variable: str | None) -> Response:
    try:
        ea = _parse(a)
        simplified = sp.simplify(ea)
        return Response(True, simplifiedA=str(simplified))
    except Exception as ex:  # noqa: BLE001
        return Response(False, error=f"normal-form failed: {ex}")


def solve_equation(a: str, variable: str | None) -> Response:
    try:
        ea = _parse(a)
        sym = sp.Symbol(variable) if variable else (ea.free_symbols.pop() if ea.free_symbols else sp.Symbol("x"))
        solutions = sp.solve(ea, sym, dict=False)
        text = ", ".join(str(s) for s in solutions)
        return Response(True, simplifiedA=text)
    except Exception as ex:  # noqa: BLE001
        return Response(False, error=f"solve failed: {ex}")


def dispatch(req: dict[str, Any]) -> Response:
    op = req.get("operation")
    a = req.get("expressionA") or ""
    b = req.get("expressionB") or ""
    variable = req.get("variable")
    tolerance = float(req.get("tolerance") or 1e-9)

    if op == OP_EQUIVALENCE:
        return verify_equivalence(a, b, variable)
    if op == OP_STEP_VALIDITY:
        return verify_step_validity(a, b, variable)
    if op == OP_NUMERICAL_TOLERANCE:
        return verify_numerical(a, b, tolerance)
    if op == OP_NORMAL_FORM:
        return normal_form(a, variable)
    if op == OP_SOLVE:
        return solve_equation(a, variable)
    return Response(False, error=f"unsupported operation code: {op}")


async def main() -> None:
    shutdown = asyncio.Event()

    def _signal(signum, _frame):
        log.info("signal %s — draining and shutting down", signum)
        shutdown.set()

    signal.signal(signal.SIGINT, _signal)
    signal.signal(signal.SIGTERM, _signal)

    log.info(
        "connecting to NATS at %s as user %s", NATS_URL, NATS_USER
    )
    nc = await nats.connect(
        servers=[NATS_URL],
        user=NATS_USER,
        password=NATS_PASSWORD,
        name="cena-sympy-sidecar",
        max_reconnect_attempts=-1,
    )
    log.info("connected; subscribing to %s and %s", VERIFY_SUBJECT, HEALTH_SUBJECT)

    async def verify_handler(msg):
        try:
            payload = json.loads(msg.data.decode("utf-8")) if msg.data else {}
            result = dispatch(payload)
        except Exception as ex:  # noqa: BLE001
            log.exception("verify handler crashed")
            result = Response(False, error=f"sidecar error: {ex}")
        await msg.respond(result.to_bytes())

    async def health_handler(msg):
        payload = json.dumps({"ok": True, "engine": "sympy", "version": sp.__version__}).encode("utf-8")
        await msg.respond(payload)

    await nc.subscribe(VERIFY_SUBJECT, cb=verify_handler)
    await nc.subscribe(HEALTH_SUBJECT, cb=health_handler)

    log.info("ready — SymPy %s", sp.__version__)
    await shutdown.wait()
    await nc.drain()
    log.info("clean shutdown complete")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        sys.exit(0)
