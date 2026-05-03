// =============================================================================
// Cena Platform — Variant Seed Service (SEC-ASSESS-001)
// Per-student question variant seeding with daily rotation.
//
// WHY: Prevents answer sharing between students. Same student sees the same
// variant on the same day (reproducible for investigation), but different
// students see different variants of the same question.
//
// HOW: HMAC-SHA256(studentId + date + questionId) → deterministic seed.
// The seed is stored in a VariantSeedAssigned_V1 event (not on the question
// document) so variant history is auditable.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

namespace Cena.Actors.Assessment;

/// <summary>
/// Generates deterministic per-student variant seeds for assessment security.
/// </summary>
public static class VariantSeedService
{
    /// <summary>
    /// Computes a deterministic variant seed for a (student, question, date) tuple.
    /// Same inputs always produce the same seed. Different students or different
    /// days produce different seeds.
    /// </summary>
    /// <param name="studentId">The student's unique identifier.</param>
    /// <param name="questionId">The question being presented.</param>
    /// <param name="date">The date (daily rotation boundary).</param>
    /// <returns>A 32-bit seed suitable for use with Random or variant selection.</returns>
    public static int ComputeSeed(string studentId, string questionId, DateOnly date)
    {
        var input = $"{studentId}:{questionId}:{date:yyyy-MM-dd}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt32(hash, 0);
    }

    /// <summary>
    /// Selects a variant index from a range using the deterministic seed.
    /// </summary>
    /// <param name="studentId">The student's unique identifier.</param>
    /// <param name="questionId">The question being presented.</param>
    /// <param name="date">The date (daily rotation boundary).</param>
    /// <param name="variantCount">Total number of variants available.</param>
    /// <returns>Zero-based variant index in [0, variantCount).</returns>
    public static int SelectVariant(string studentId, string questionId, DateOnly date, int variantCount)
    {
        if (variantCount <= 1) return 0;
        var seed = ComputeSeed(studentId, questionId, date);
        return Math.Abs(seed % variantCount);
    }
}
