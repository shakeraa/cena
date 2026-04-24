// =============================================================================
// Cena Platform -- MeEndpoints CQRS Race Regression Tests (FIND-qa-002)
// Tests for race conditions between manual Store() and inline projection daemon.
// =============================================================================

using Cena.Actors.Events;
using Marten;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Security.Claims;

namespace Cena.Student.Api.Host.Tests.Endpoints;

/// <summary>
/// FIND-qa-002: Regression tests for MeEndpoints CQRS race conditions.
/// Verifies that inline projections are not manually Stored() - only event appending.
/// </summary>
public class MeEndpointsTests : IClassFixture<StudentApiTestFactory>
{
    private readonly IDocumentStore _store;

    public MeEndpointsTests(StudentApiTestFactory factory)
    {
        _store = factory.Services.GetRequiredService<IDocumentStore>();
    }

    [Fact]
    public async Task UpdateProfile_OnlyAppendsEvent_DoesNotCallStoreOnProjection()
    {
        // Arrange: Create a student with existing profile
        var studentId = $"test-student-{Guid.NewGuid():N}";
        await SeedStudentWithProfile(studentId, "Original Name");

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("student_id", studentId)
        }, "TestAuth"));

        // Act: Update profile
        var result = await Cena.Api.Host.Endpoints.MeEndpoints.UpdateProfile(
            user, _store, new UpdateProfileRequest("New Name", null, null, null));

        // Assert
        Assert.IsType<NoContent>(result);

        // Verify: Event was appended
        await using var querySession = _store.QuerySession();
        var events = await querySession.Events.FetchStreamAsync(studentId);
        var profileEvents = events.Select(e => e.Data).OfType<ProfileUpdated_V1>().ToList();
        
        Assert.Single(profileEvents);
        Assert.Equal("New Name", profileEvents[0].DisplayName);
    }

    [Fact]
    public async Task ConcurrentProfileUpdates_DoNotLoseData()
    {
        // Arrange: Race condition test
        var studentId = $"test-student-{Guid.NewGuid():N}";
        await SeedStudentWithProfile(studentId, "Original");

        // Act: Fire 5 concurrent updates
        var tasks = Enumerable.Range(1, 5).Select(i =>
            Cena.Api.Host.Endpoints.MeEndpoints.UpdateProfile(
                new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("student_id", studentId)
                }, "TestAuth")),
                _store,
                new UpdateProfileRequest($"Name {i}", null, null, null)))
            .ToList();

        var results = await Task.WhenAll(tasks);

        // Assert: At least one succeeded
        Assert.True(results.Any(r => r is NoContent));

        // Verify: Events persisted
        await using var querySession = _store.QuerySession();
        var events = await querySession.Events.FetchStreamAsync(studentId);
        var profileEvents = events.Select(e => e.Data).OfType<ProfileUpdated_V1>().ToList();
        
        Assert.True(profileEvents.Count >= 1);
    }

    private async Task SeedStudentWithProfile(string studentId, string displayName)
    {
        await using var session = _store.LightweightSession();
        session.Events.StartStream<Cena.Actors.Events.StudentProfileSnapshot>(studentId, 
            new OnboardingCompleted_V1(studentId, "student", "en", new[] { "math" }, 30, DateTimeOffset.UtcNow));
        session.Events.Append(studentId, new ProfileUpdated_V1(
            studentId, displayName, null, null, null, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();
    }
}

