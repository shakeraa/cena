// =============================================================================
// Cena Platform — Marten registration for the Challenge / Gamification
// bounded context (PRR-304)
//
// Extracted from MartenConfiguration.cs as part of the PRR-304 LOC drain.
// Mirrors the per-bounded-context pattern established by
// MockExamRunMartenRegistration.RegisterMockExamRunContext.
//
// Documents registered here cover the boss-battle / daily-challenge /
// card-chain / tournament gamification surfaces:
//   - BossAttemptDocument             (per-student boss attempt log)
//   - DailyChallengeDocument          (daily catalog rows)
//   - DailyChallengeCompletionDocument(per-student daily completion)
//   - CardChainDefinitionDocument     (card-chain definitions)
//   - CardChainProgressDocument       (per-student chain progress)
//   - TournamentDocument              (tournament catalog)
//   - TournamentRegistrationDocument  (per-student tournament entry)
//
// Behaviour-preserving extract: indexes + Identity expressions are
// identical to the pre-extract definitions in MartenConfiguration.cs.
// =============================================================================

using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Configuration;

/// <summary>
/// Marten registrations for the Challenge / Gamification bounded context
/// (boss attempts, daily challenges, card chains, tournaments). Called
/// from <see cref="MartenConfiguration.ConfigureCenaEventStore"/>.
/// </summary>
public static class MartenChallengeRegistration
{
    /// <summary>
    /// Register all Challenge-context documents with Marten. Idempotent.
    /// </summary>
    public static void RegisterChallengeContext(this StoreOptions opts)
    {
        // ── Boss Attempt Document (STB-05b) ──
        opts.Schema.For<BossAttemptDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.BossBattleId)
            .Index(x => x.Date);

        // ── Challenge Catalog Documents (HARDEN STB-05) ──
        opts.Schema.For<DailyChallengeDocument>()
            .Identity(x => x.Id)
            .Index(x => x.Date)
            .Index(x => x.Locale);

        opts.Schema.For<DailyChallengeCompletionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.Date)
            .Index(x => x.Score);

        opts.Schema.For<CardChainDefinitionDocument>()
            .Identity(x => x.Id)
            .Index(x => x.ChainId)
            .Index(x => x.Subject);

        opts.Schema.For<CardChainProgressDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.ChainId);

        opts.Schema.For<TournamentDocument>()
            .Identity(x => x.Id)
            .Index(x => x.IsActive)
            .Index(x => x.StartsAt)
            .Index(x => x.EndsAt);

        opts.Schema.For<TournamentRegistrationDocument>()
            .Identity(x => x.Id)
            .Index(x => x.StudentId)
            .Index(x => x.TournamentId);
    }
}
