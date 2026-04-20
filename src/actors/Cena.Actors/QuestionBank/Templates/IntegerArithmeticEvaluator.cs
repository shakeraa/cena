// =============================================================================
// Cena Platform — Integer-Rational Arithmetic Evaluator (prr-200)
//
// Tiny recursive-descent parser for pure integer arithmetic over +, -, *, /
// with parens and unary signs. NOT a CAS — it only accepts integer literal
// grammars, and returns (num, den) as a reduced-or-unreduced rational.
// Used by:
//   * Cena.Tools.QuestionGen (offline renderer) to canonicalise a
//     substituted solution text without the SymPy sidecar.
//   * Test-only FakeParametricRenderer for the same purpose.
//
// Expressions this evaluator can handle:
//   "5", "-3", "(c - b) / a" AFTER substitution → "(9 - 6) / 8", "(1/3) + 2"
// Expressions it cannot handle (returns false):
//   anything with identifiers, symbolic constants, exponents (^ or **), or
//   functions (sqrt, Abs, etc.). Such expressions pass through to the caller
//   as "symbolic" shape.
//
// No LLM import — in-scope for NoLlmInParametricPipelineTest.
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

public static class IntegerArithmeticEvaluator
{
    /// <summary>
    /// Attempt to evaluate <paramref name="expr"/> as an integer-rational
    /// arithmetic expression. Returns true with the result in (num, den)
    /// when successful. Denominator is zero when the expression evaluates
    /// to a division-by-zero; callers should treat that as a zero-divisor
    /// rejection.
    /// </summary>
    public static bool TryEvaluate(string expr, out long num, out long den)
    {
        num = 0; den = 1;
        if (string.IsNullOrWhiteSpace(expr)) return false;
        try
        {
            var p = new Parser(expr);
            var (n, d) = p.ParseExpression();
            if (!p.AtEnd) return false;
            num = n; den = d;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        public Parser(string s) { _s = s; _i = 0; SkipWs(); }
        public bool AtEnd { get { SkipWs(); return _i >= _s.Length; } }

        public (long n, long d) ParseExpression()
        {
            var (n, d) = ParseTerm();
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length) break;
                var c = _s[_i];
                if (c != '+' && c != '-') break;
                _i++;
                var (n2, d2) = ParseTerm();
                (n, d) = c == '+'
                    ? (n * d2 + n2 * d, d * d2)
                    : (n * d2 - n2 * d, d * d2);
            }
            return (n, d);
        }

        private (long n, long d) ParseTerm()
        {
            var (n, d) = ParseFactor();
            while (true)
            {
                SkipWs();
                if (_i >= _s.Length) break;
                var c = _s[_i];
                if (c != '*' && c != '/') break;
                _i++;
                var (n2, d2) = ParseFactor();
                (n, d) = c == '*'
                    ? (n * n2, d * d2)
                    : (n * d2, d * n2);
            }
            return (n, d);
        }

        private (long n, long d) ParseFactor()
        {
            SkipWs();
            if (_i >= _s.Length) throw new FormatException("unexpected end");
            var c = _s[_i];
            if (c == '+' || c == '-')
            {
                _i++;
                var (n, d) = ParseFactor();
                return c == '-' ? (-n, d) : (n, d);
            }
            if (c == '(')
            {
                _i++;
                var (n, d) = ParseExpression();
                SkipWs();
                if (_i >= _s.Length || _s[_i] != ')') throw new FormatException("expected ')'");
                _i++;
                return (n, d);
            }
            if (char.IsDigit(c))
            {
                var start = _i;
                while (_i < _s.Length && char.IsDigit(_s[_i])) _i++;
                var lit = _s.Substring(start, _i - start);
                return (long.Parse(lit, System.Globalization.CultureInfo.InvariantCulture), 1);
            }
            throw new FormatException($"unexpected '{c}' at {_i}");
        }

        private void SkipWs()
        {
            while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t')) _i++;
        }
    }
}
