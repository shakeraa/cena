// =============================================================================
// Cena Platform -- ML Training Exclusion Enforcement Tests
// RDY-006 / ADR-0003 Decision 3: Ensures [MlExcluded] event types are
// excluded from GDPR exports and any training data pipeline.
//
// These tests are architectural guardrails — they scan assemblies via
// reflection to verify that the exclusion contract is enforced at
// compile-time, preventing accidental inclusion of misconception data
// in ML corpora ("Affected Work Product" per Edmodo decree).
// =============================================================================

using System.Reflection;
using Cena.Actors.Events;
using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Tests.Compliance;

public sealed class MlExclusionEnforcementTests
{
    private static readonly Assembly EventsAssembly = typeof(MisconceptionDetected_V1).Assembly;
    private static readonly Assembly InfraAssembly = typeof(MlExcludedAttribute).Assembly;

    /// <summary>
    /// All three misconception event types must carry [MlExcluded].
    /// If someone adds a new misconception event without the attribute,
    /// this test fails and blocks the build.
    /// </summary>
    [Fact]
    public void All_misconception_event_types_carry_MlExcluded_attribute()
    {
        var misconceptionTypes = new[]
        {
            typeof(MisconceptionDetected_V1),
            typeof(MisconceptionRemediated_V1),
            typeof(SessionMisconceptionsScrubbed_V1)
        };

        foreach (var type in misconceptionTypes)
        {
            var attr = type.GetCustomAttribute<MlExcludedAttribute>();
            Assert.NotNull(attr);
            Assert.Contains("ADR-0003", attr!.Reason);
        }
    }

    /// <summary>
    /// Any domain event type (IDelegatedEvent) in the Cena.Actors.Events namespace
    /// whose name contains "Misconception" must have [MlExcluded]. This catches
    /// future misconception events that forget the attribute.
    /// Non-event types (catalog entries, tallies) are excluded from this check.
    /// </summary>
    [Fact]
    public void All_types_named_Misconception_in_events_assembly_are_MlExcluded()
    {
        var misconceptionTypes = EventsAssembly.GetTypes()
            .Where(t => t.Namespace == "Cena.Actors.Events")
            .Where(t => t.Name.Contains("Misconception", StringComparison.OrdinalIgnoreCase))
            .Where(t => t.IsClass || (t.IsValueType && !t.IsEnum))
            .ToList();

        Assert.NotEmpty(misconceptionTypes);

        foreach (var type in misconceptionTypes)
        {
            var attr = type.GetCustomAttribute<MlExcludedAttribute>();
            Assert.True(attr != null,
                $"Type '{type.Name}' contains 'Misconception' but lacks [MlExcluded]. " +
                "ADR-0003 Decision 3 requires all misconception event types to be ML-excluded.");
        }
    }

    /// <summary>
    /// StudentDataExporter.IsMlExcluded returns true for all [MlExcluded] types.
    /// </summary>
    [Theory]
    [InlineData(typeof(MisconceptionDetected_V1), true)]
    [InlineData(typeof(MisconceptionRemediated_V1), true)]
    [InlineData(typeof(SessionMisconceptionsScrubbed_V1), true)]
    [InlineData(typeof(ConceptAttempted_V1), false)]
    [InlineData(typeof(ConceptMastered_V1), false)]
    [InlineData(typeof(LearningSessionStarted_V1), false)]
    public void IsMlExcluded_returns_correct_value(Type eventType, bool expected)
    {
        Assert.Equal(expected, StudentDataExporter.IsMlExcluded(eventType));
    }

    /// <summary>
    /// ADR-0003 Decision 3: no [MlExcluded] type may appear in any method
    /// whose name contains "Training", "Finetune", "RLHF", or "Embedding"
    /// within the Infrastructure assembly. This is a heuristic guardrail
    /// that catches accidental pipeline inclusion.
    /// </summary>
    [Fact]
    public void No_MlExcluded_types_referenced_in_training_pipeline_methods()
    {
        var mlExcludedTypes = EventsAssembly.GetTypes()
            .Where(t => t.GetCustomAttribute<MlExcludedAttribute>() != null)
            .Select(t => t.Name)
            .ToHashSet();

        var trainingMethodKeywords = new[] { "Training", "Finetune", "RLHF", "Embedding" };

        var infraMethods = InfraAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                          BindingFlags.Instance | BindingFlags.Static))
            .Where(m => trainingMethodKeywords.Any(k =>
                m.Name.Contains(k, StringComparison.OrdinalIgnoreCase)));

        foreach (var method in infraMethods)
        {
            var paramTypes = method.GetParameters()
                .Select(p => p.ParameterType.Name)
                .Concat(new[] { method.ReturnType.Name });

            foreach (var paramTypeName in paramTypes)
            {
                Assert.False(mlExcludedTypes.Contains(paramTypeName),
                    $"Method '{method.DeclaringType?.Name}.{method.Name}' references " +
                    $"ML-excluded type '{paramTypeName}'. ADR-0003 Decision 3 prohibits " +
                    "misconception data in training pipelines.");
            }
        }
    }

    /// <summary>
    /// ADR-0003 Decision 1: StudentState must NEVER contain misconception fields.
    /// Misconception state lives in LearningSessionState only.
    /// </summary>
    [Fact]
    public void StudentState_has_no_misconception_fields()
    {
        var studentStateType = EventsAssembly.GetType("Cena.Actors.Students.StudentState");
        Assert.NotNull(studentStateType);

        var props = studentStateType!.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in props)
        {
            Assert.False(
                prop.Name.Contains("Misconception", StringComparison.OrdinalIgnoreCase),
                $"StudentState.{prop.Name} violates ADR-0003 Decision 1: " +
                "misconception state must be session-scoped, never on student profile.");
        }
    }

    /// <summary>
    /// ADR-0003 Decision 2: Session misconception retention must be 30 days,
    /// with a hard cap of 90 days.
    /// </summary>
    [Fact]
    public void Retention_policy_enforces_30_day_default_and_90_day_cap()
    {
        Assert.Equal(TimeSpan.FromDays(30), DataRetentionPolicy.SessionMisconceptionRetention);
        Assert.Equal(TimeSpan.FromDays(90), DataRetentionPolicy.SessionMisconceptionHardCap);
        Assert.True(DataRetentionPolicy.SessionMisconceptionRetention <=
                     DataRetentionPolicy.SessionMisconceptionHardCap);
    }

    /// <summary>
    /// Every [MlExcluded] attribute must include a Reason referencing an ADR.
    /// Reason-less exclusions are banned — legal traceability is mandatory.
    /// </summary>
    [Fact]
    public void MlExcluded_attribute_requires_non_empty_reason()
    {
        var mlExcludedTypes = EventsAssembly.GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<MlExcludedAttribute>()))
            .Where(x => x.Attr != null);

        foreach (var (type, attr) in mlExcludedTypes)
        {
            Assert.False(string.IsNullOrWhiteSpace(attr!.Reason),
                $"[MlExcluded] on '{type.Name}' has an empty Reason. " +
                "All exclusions must cite a legal basis (ADR, regulation, etc.).");
        }
    }

    /// <summary>
    /// DataCategory.SessionMisconception must exist in the enum.
    /// Prevents someone from accidentally removing it.
    /// </summary>
    [Fact]
    public void DataCategory_includes_SessionMisconception()
    {
        Assert.True(Enum.IsDefined(
            typeof(Cena.Infrastructure.Compliance.DataCategory),
            Cena.Infrastructure.Compliance.DataCategory.SessionMisconception));
    }
}
