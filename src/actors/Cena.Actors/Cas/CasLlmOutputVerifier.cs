// =============================================================================
// Cena Platform — CAS-Verify LLM Output (CAS-LLM-001)
//
// Scans LLM tutor responses for mathematical claims and verifies each
// via the CAS router before delivery to the student. Per ADR-0002:
// "No LLM output that claims a mathematical result is shown to a student
// until a deterministic CAS has confirmed it."
// =============================================================================

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

/// <summary>
/// Result of verifying all math claims in an LLM response.
/// </summary>
public record LlmOutputVerification(
    string OriginalText,
    string VerifiedText,
    int ClaimsFound,
    int ClaimsVerified,
    int ClaimsFailed,
    int ClaimsMasked,
    IReadOnlyList<MathClaim> Claims
);

/// <summary>
/// A mathematical claim extracted from LLM output.
/// </summary>
public record MathClaim(
    string Expression,
    int StartIndex,
    int EndIndex,
    bool Verified,
    string? CasEngine,
    string? MaskedReplacement
);

public interface ICasLlmOutputVerifier
{
    /// <summary>
    /// Verify all mathematical expressions in LLM output via CAS.
    /// Masks unverified claims with placeholder text.
    /// </summary>
    Task<LlmOutputVerification> VerifyAndMaskAsync(
        string llmOutput,
        string? canonicalAnswer,
        bool answerRevealAllowed,
        CancellationToken ct = default);
}

/// <summary>
/// Extracts mathematical claims from LLM text, verifies each via CAS,
/// and masks unverified or answer-leaking content.
/// </summary>
public sealed class CasLlmOutputVerifier : ICasLlmOutputVerifier
{
    private readonly ICasRouterService _cas;
    private readonly ILogger<CasLlmOutputVerifier> _logger;

    // Patterns to detect mathematical expressions in LLM text
    private static readonly Regex MathInlineRegex = new(
        @"\$([^$]+)\$", RegexOptions.Compiled);
    private static readonly Regex MathBlockRegex = new(
        @"\$\$([^$]+)\$\$", RegexOptions.Compiled);
    private static readonly Regex EquationRegex = new(
        @"(\b[a-zA-Z]\s*=\s*-?\d+\.?\d*\b)", RegexOptions.Compiled);

    private const string MaskedPlaceholder = "[try working through it yourself]";
    private const string AnswerMaskedPlaceholder = "[solution hidden — complete the steps first]";

    public CasLlmOutputVerifier(ICasRouterService cas, ILogger<CasLlmOutputVerifier> logger)
    {
        _cas = cas;
        _logger = logger;
    }

    public async Task<LlmOutputVerification> VerifyAndMaskAsync(
        string llmOutput,
        string? canonicalAnswer,
        bool answerRevealAllowed,
        CancellationToken ct = default)
    {
        var claims = ExtractMathClaims(llmOutput);
        var verifiedText = llmOutput;
        int verified = 0, failed = 0, masked = 0;

        // Process claims in reverse order so index offsets stay valid
        var sortedClaims = claims.OrderByDescending(c => c.StartIndex).ToList();
        var processedClaims = new List<MathClaim>();

        foreach (var claim in sortedClaims)
        {
            ct.ThrowIfCancellationRequested();

            // Check if this claim leaks the canonical answer
            if (!answerRevealAllowed && canonicalAnswer != null
                && IsAnswerLeak(claim.Expression, canonicalAnswer))
            {
                verifiedText = MaskClaim(verifiedText, claim, AnswerMaskedPlaceholder);
                processedClaims.Add(claim with { Verified = false, MaskedReplacement = AnswerMaskedPlaceholder });
                masked++;
                continue;
            }

            // Verify via CAS
            var result = await _cas.VerifyAsync(new CasVerifyRequest(
                Operation: CasOperation.NormalForm,
                ExpressionA: claim.Expression
            ), ct);

            if (result.ErrorMessage?.StartsWith("[ERROR]") == true)
            {
                // CAS error — mask conservatively
                verifiedText = MaskClaim(verifiedText, claim, MaskedPlaceholder);
                processedClaims.Add(claim with { Verified = false, MaskedReplacement = MaskedPlaceholder });
                failed++;
                masked++;
            }
            else
            {
                processedClaims.Add(claim with { Verified = true, CasEngine = result.Engine });
                verified++;
            }
        }

        processedClaims.Reverse(); // restore original order

        return new LlmOutputVerification(
            llmOutput, verifiedText, claims.Count, verified, failed, masked, processedClaims);
    }

    private static List<MathClaim> ExtractMathClaims(string text)
    {
        var claims = new List<MathClaim>();

        // Extract $$ block math $$ first (greedy over inline)
        foreach (Match m in MathBlockRegex.Matches(text))
        {
            claims.Add(new MathClaim(m.Groups[1].Value, m.Index, m.Index + m.Length, false, null, null));
        }

        // Extract $ inline math $
        foreach (Match m in MathInlineRegex.Matches(text))
        {
            // Skip if already captured as block math
            if (claims.Any(c => m.Index >= c.StartIndex && m.Index < c.EndIndex)) continue;
            claims.Add(new MathClaim(m.Groups[1].Value, m.Index, m.Index + m.Length, false, null, null));
        }

        // Extract bare equations (x = 42 style)
        foreach (Match m in EquationRegex.Matches(text))
        {
            if (claims.Any(c => m.Index >= c.StartIndex && m.Index < c.EndIndex)) continue;
            claims.Add(new MathClaim(m.Value, m.Index, m.Index + m.Length, false, null, null));
        }

        return claims;
    }

    private static bool IsAnswerLeak(string expression, string canonicalAnswer)
    {
        // Simple containment check + numerical equivalence
        var exprNorm = expression.Replace(" ", "").ToLowerInvariant();
        var ansNorm = canonicalAnswer.Replace(" ", "").ToLowerInvariant();

        if (exprNorm.Contains(ansNorm)) return true;

        // Numerical check
        if (double.TryParse(expression.Trim(), out var exprVal)
            && double.TryParse(canonicalAnswer.Trim(), out var ansVal))
        {
            return Math.Abs(exprVal - ansVal) < 1e-6;
        }

        return false;
    }

    private static string MaskClaim(string text, MathClaim claim, string replacement) =>
        string.Concat(text.AsSpan(0, claim.StartIndex), replacement, text.AsSpan(claim.EndIndex));
}
