// =============================================================================
// Cena Platform -- Consent Enforcement Regression Tests
// FIND-privacy-007: Consent system must actually gate data processing
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Infrastructure.Tests.Compliance;

public class ConsentEnforcementTests
{
    private readonly TestGdprConsentManager _consentManager;
    private readonly RequiresConsentAttribute _peerComparisonFilter;
    private readonly RequiresConsentAttribute _socialFeaturesFilter;
    private readonly RequiresConsentAttribute _thirdPartyAiFilter;

    public ConsentEnforcementTests()
    {
        _consentManager = new TestGdprConsentManager();
        _peerComparisonFilter = new RequiresConsentAttribute(ProcessingPurpose.PeerComparison);
        _socialFeaturesFilter = new RequiresConsentAttribute(ProcessingPurpose.SocialFeatures);
        _thirdPartyAiFilter = new RequiresConsentAttribute(ProcessingPurpose.ThirdPartyAi);
    }

    [Fact]
    public async Task RequiresConsent_WithoutConsent_Returns403WithError()
    {
        // Arrange
        var studentId = "student-123";
        var httpContext = CreateHttpContext(studentId);
        var endpoint = CreateEndpoint(_peerComparisonFilter);
        httpContext.SetEndpoint(endpoint);

        // No consent recorded - should deny

        // Act
        var result = await _peerComparisonFilter.InvokeAsync(
            httpContext,
            _ => Task.FromResult<object?>(Results.Ok()),
            new NullLogger<RequiresConsentAttribute>());

        // Assert
        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public async Task RequiresConsent_WithConsent_CallsNextHandler()
    {
        // Arrange
        var studentId = "student-456";
        var httpContext = CreateHttpContext(studentId);
        var endpoint = CreateEndpoint(_peerComparisonFilter);
        httpContext.SetEndpoint(endpoint);

        // Record consent
        await _consentManager.RecordConsentAsync(studentId, ConsentType.Analytics);

        var nextCalled = false;

        // Act
        var result = await _peerComparisonFilter.InvokeAsync(
            httpContext,
            _ =>
            {
                nextCalled = true;
                return Task.FromResult<object?>(Results.Ok());
            },
            new NullLogger<RequiresConsentAttribute>());

        // Assert
        Assert.True(nextCalled, "Next handler should be called when consent is granted");
        Assert.IsType<Ok>(result);
    }

    [Theory]
    [InlineData(ProcessingPurpose.PeerComparison, "peer_comparison")]
    [InlineData(ProcessingPurpose.SocialFeatures, "social_features")]
    [InlineData(ProcessingPurpose.ThirdPartyAi, "third_party_ai")]
    [InlineData(ProcessingPurpose.BehavioralAnalytics, "behavioral_analytics")]
    public void ProcessingPurpose_ToString_ReturnsExpectedValue(ProcessingPurpose purpose, string expected)
    {
        Assert.Equal(expected, purpose.ToString().ToLowerInvariant());
    }

    [Fact]
    public void ProcessingPurpose_AccountAuth_IsAlwaysRequired()
    {
        Assert.True(ProcessingPurpose.AccountAuth.IsAlwaysRequired());
        Assert.True(ProcessingPurpose.SessionContinuity.IsAlwaysRequired());
    }

    [Fact]
    public void ProcessingPurpose_NonRequiredPurposes_AreNotAlwaysRequired()
    {
        Assert.False(ProcessingPurpose.PeerComparison.IsAlwaysRequired());
        Assert.False(ProcessingPurpose.SocialFeatures.IsAlwaysRequired());
        Assert.False(ProcessingPurpose.ThirdPartyAi.IsAlwaysRequired());
        Assert.False(ProcessingPurpose.BehavioralAnalytics.IsAlwaysRequired());
    }

    [Fact]
    public void ProcessingPurpose_GetDefaultConsent_Adult_ReturnsExpectedDefaults()
    {
        // Act & Assert - Adult defaults (isMinor = false)
        Assert.True(ProcessingPurpose.AccountAuth.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.SessionContinuity.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.AdaptiveRecommendation.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.PeerComparison.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.LeaderboardDisplay.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.SocialFeatures.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.ThirdPartyAi.GetDefaultConsent(false));
        Assert.True(ProcessingPurpose.BehavioralAnalytics.GetDefaultConsent(false));
        Assert.False(ProcessingPurpose.CrossTenantBenchmarking.GetDefaultConsent(false));
        Assert.False(ProcessingPurpose.MarketingNudges.GetDefaultConsent(false));
    }

    [Fact]
    public void ProcessingPurpose_GetDefaultConsent_Minor_ReturnsHighPrivacyDefaults()
    {
        // Act & Assert - Minor defaults (isMinor = true) - high privacy
        Assert.True(ProcessingPurpose.AccountAuth.GetDefaultConsent(true)); // Required
        Assert.True(ProcessingPurpose.SessionContinuity.GetDefaultConsent(true)); // Required
        Assert.True(ProcessingPurpose.AdaptiveRecommendation.GetDefaultConsent(true)); // Learning
        Assert.False(ProcessingPurpose.PeerComparison.GetDefaultConsent(true)); // Social - off
        Assert.False(ProcessingPurpose.LeaderboardDisplay.GetDefaultConsent(true)); // Social - off
        Assert.False(ProcessingPurpose.SocialFeatures.GetDefaultConsent(true)); // Social - off
        Assert.False(ProcessingPurpose.ThirdPartyAi.GetDefaultConsent(true)); // AI - off for minors
        Assert.False(ProcessingPurpose.BehavioralAnalytics.GetDefaultConsent(true)); // Tracking - off
        Assert.False(ProcessingPurpose.CrossTenantBenchmarking.GetDefaultConsent(false));
        Assert.False(ProcessingPurpose.MarketingNudges.GetDefaultConsent(true)); // Marketing - off
    }

    [Fact]
    public async Task FullWorkflow_DenyThenGrant_ConsentGatesAccess()
    {
        // Arrange
        var studentId = "student-workflow-789";
        var httpContext = CreateHttpContext(studentId);
        var endpoint = CreateEndpoint(_thirdPartyAiFilter);
        httpContext.SetEndpoint(endpoint);

        // Phase 1: No consent - should be denied
        var result1 = await _thirdPartyAiFilter.InvokeAsync(
            httpContext,
            _ => Task.FromResult<object?>(Results.Ok()),
            new NullLogger<RequiresConsentAttribute>());

        Assert.IsType<ForbidHttpResult>(result1);

        // Phase 2: Grant consent
        await _consentManager.RecordConsentAsync(studentId, ConsentType.ThirdParty);

        // Phase 3: With consent - should succeed
        var nextCalled = false;
        var result2 = await _thirdPartyAiFilter.InvokeAsync(
            httpContext,
            _ =>
            {
                nextCalled = true;
                return Task.FromResult<object?>(Results.Ok());
            },
            new NullLogger<RequiresConsentAttribute>());

        Assert.True(nextCalled);
        Assert.IsType<Ok>(result2);

        // Phase 4: Revoke consent
        await _consentManager.RevokeConsentAsync(studentId, ConsentType.ThirdParty);

        // Phase 5: After revoke - should be denied again
        var result3 = await _thirdPartyAiFilter.InvokeAsync(
            httpContext,
            _ => Task.FromResult<object?>(Results.Ok()),
            new NullLogger<RequiresConsentAttribute>());

        Assert.IsType<ForbidHttpResult>(result3);
    }

    [Fact]
    public void ConsentChangeLog_CapturesAllRequiredFields()
    {
        // Arrange
        var log = new ConsentChangeLog
        {
            Id = Guid.NewGuid(),
            StudentId = "student-001",
            Purpose = ProcessingPurpose.SocialFeatures,
            PreviousValue = false,
            NewValue = true,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = "student:self",
            Source = ConsentChangeSource.UI
        };

        // Assert
        Assert.Equal("student-001", log.StudentId);
        Assert.Equal(ProcessingPurpose.SocialFeatures, log.Purpose);
        Assert.False(log.PreviousValue);
        Assert.True(log.NewValue);
        Assert.Equal("student:self", log.ChangedBy);
        Assert.Equal(ConsentChangeSource.UI, log.Source);
    }

    [Fact]
    public void ProcessingPurpose_GetDescription_ReturnsHumanReadableText()
    {
        // Act & Assert
        Assert.Contains("authentication", ProcessingPurpose.AccountAuth.GetDescription(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("session", ProcessingPurpose.SessionContinuity.GetDescription(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI", ProcessingPurpose.ThirdPartyAi.GetDescription());
        Assert.Contains("analytics", ProcessingPurpose.BehavioralAnalytics.GetDescription(), StringComparison.OrdinalIgnoreCase);
    }

    // Helper methods

    private static HttpContext CreateHttpContext(string studentId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", studentId),
            new Claim(ClaimTypes.NameIdentifier, studentId)
        }, "TestAuth"));
        return context;
    }

    private static Endpoint CreateEndpoint(params object[] metadata)
    {
        return new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(metadata),
            "Test Endpoint");
    }
}

/// <summary>
/// Test implementation of IGdprConsentManager for unit testing
/// </summary>
public class TestGdprConsentManager : IGdprConsentManager
{
    private readonly Dictionary<string, HashSet<ConsentType>> _consents = new();

    public Task<IReadOnlyList<ConsentRecord>> GetConsentsAsync(string studentId)
    {
        var list = _consents.TryGetValue(studentId, out var consents)
            ? consents.Select(c => new ConsentRecord
            {
                StudentId = studentId,
                ConsentType = c,
                GrantedAt = DateTimeOffset.UtcNow,
                IsRevoked = false
            }).ToList()
            : new List<ConsentRecord>();

        return Task.FromResult<IReadOnlyList<ConsentRecord>>(list);
    }

    public Task<bool> HasConsentAsync(string studentId, ConsentType type)
    {
        var hasConsent = _consents.TryGetValue(studentId, out var consents)
            && consents.Contains(type);
        return Task.FromResult(hasConsent);
    }

    public Task RecordConsentAsync(string studentId, ConsentType type, string? recordedBy = null)
    {
        if (!_consents.TryGetValue(studentId, out var consents))
        {
            consents = new HashSet<ConsentType>();
            _consents[studentId] = consents;
        }
        consents.Add(type);
        return Task.CompletedTask;
    }

    public Task RevokeConsentAsync(string studentId, ConsentType type)
    {
        if (_consents.TryGetValue(studentId, out var consents))
        {
            consents.Remove(type);
        }
        return Task.CompletedTask;
    }

    public Task RecordConsentChangeAsync(string studentId, ProcessingPurpose purpose, bool granted, string recordedBy, ConsentChangeSource source)
    {
        // Simplified for testing
        var consentType = MapPurposeToConsentType(purpose);
        if (granted)
            return RecordConsentAsync(studentId, consentType, recordedBy);
        else
            return RevokeConsentAsync(studentId, consentType);
    }

    public Task<IReadOnlyDictionary<ProcessingPurpose, bool>> GetDefaultConsentsAsync(bool isMinor)
    {
        var defaults = Enum.GetValues<ProcessingPurpose>()
            .ToDictionary(
                p => p,
                p => p.GetDefaultConsent(isMinor));
        return Task.FromResult<IReadOnlyDictionary<ProcessingPurpose, bool>>(defaults);
    }

    public Task<IReadOnlyDictionary<ProcessingPurpose, bool>> BatchCheckConsentAsync(string studentId, ProcessingPurpose[] purposes)
    {
        // Simplified - assume no consent for testing
        var result = purposes.ToDictionary(p => p, _ => false);
        return Task.FromResult<IReadOnlyDictionary<ProcessingPurpose, bool>>(result);
    }

    private static ConsentType MapPurposeToConsentType(ProcessingPurpose purpose)
    {
        return purpose switch
        {
            ProcessingPurpose.Analytics => ConsentType.Analytics,
            ProcessingPurpose.MarketingNudges => ConsentType.Marketing,
            ProcessingPurpose.ThirdPartyAi => ConsentType.ThirdParty,
            _ => ConsentType.Analytics
        };
    }
}
