// =============================================================================
// Cena Platform — Boss Battle Catalog (STB-05b)
// Static catalog of boss battles with requirements and rewards
// =============================================================================

namespace Cena.Infrastructure.Challenges;

public static class BossBattleCatalog
{
    public static IReadOnlyList<BossBattleDefinition> Bosses => new[]
    {
        new BossBattleDefinition(
            "boss_algebra_001",
            "The Algebraic Dragon",
            "A formidable foe guarding the secrets of algebra! Defeat this boss by solving a series of progressively difficult algebraic equations. Each correct answer deals damage, but mistakes cost you precious time.",
            "Mathematics",
            "medium",
            5,  // Required mastery level
            3,  // Max attempts per day
            new[]
            {
                new BossBattleReward("xp", 500),
                new BossBattleReward("badge", 1),
                new BossBattleReward("gems", 50)
            }),

        new BossBattleDefinition(
            "boss_physics_001",
            "Newton's Nemesis",
            "Master the laws of motion and forces to defeat this physics guardian. Requires deep understanding of mechanics and problem-solving under pressure.",
            "Physics",
            "hard",
            8,  // Required mastery level
            2,  // Max attempts per day
            new[]
            {
                new BossBattleReward("xp", 750),
                new BossBattleReward("badge", 1),
                new BossBattleReward("gems", 75)
            }),

        new BossBattleDefinition(
            "boss_chemistry_001",
            "The Elemental Guardian",
            "Balance equations, understand periodic trends, and master stoichiometry to overcome this chemistry challenge.",
            "Chemistry",
            "medium",
            6,  // Required mastery level
            3,  // Max attempts per day
            new[]
            {
                new BossBattleReward("xp", 600),
                new BossBattleReward("badge", 1),
                new BossBattleReward("gems", 60)
            }),

        new BossBattleDefinition(
            "boss_calculus_001",
            "The Calculus Colossus",
            "Only the most dedicated students dare face this beast. Requires mastery of limits, derivatives, and integrals.",
            "Mathematics",
            "expert",
            15, // Required mastery level
            1,  // Max attempts per day
            new[]
            {
                new BossBattleReward("xp", 1500),
                new BossBattleReward("badge", 1),
                new BossBattleReward("gems", 150)
            }),

        new BossBattleDefinition(
            "boss_biology_001",
            "The Bio-Behemoth",
            "Navigate the complexities of cellular biology, genetics, and ecology to defeat this life sciences guardian.",
            "Biology",
            "hard",
            12, // Required mastery level
            2,  // Max attempts per day
            new[]
            {
                new BossBattleReward("xp", 800),
                new BossBattleReward("badge", 1),
                new BossBattleReward("gems", 80)
            })
    };

    public static BossBattleDefinition? GetById(string id)
        => Bosses.FirstOrDefault(b => b.Id == id);
}

public record BossBattleDefinition(
    string Id,
    string Name,
    string Description,
    string Subject,
    string Difficulty,
    int RequiredMasteryLevel,
    int MaxAttemptsPerDay,
    BossBattleReward[] Rewards);

public record BossBattleReward(string Type, int Amount);
