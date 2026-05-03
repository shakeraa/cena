using System.Diagnostics.Metrics;
using Cena.Actors.Management;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Proto;

namespace Cena.Actors.Tests.Management;

/// <summary>
/// Tests for StudentActorManager pool registration, back-pressure, and drain.
/// Validates that the activation/deactivation flow works correctly
/// now that StudentActor self-registers on startup.
/// </summary>
public sealed class StudentActorManagerPoolTests
{
    private readonly StudentActorManager _manager;
    private readonly ILogger<StudentActorManager> _logger;

    public StudentActorManagerPoolTests()
    {
        _logger = Substitute.For<ILogger<StudentActorManager>>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _manager = new StudentActorManager(_logger, meterFactory);
    }

    [Fact]
    public async Task ActivateStudent_FirstActivation_ReturnsSuccess()
    {
        var system = new ActorSystem();
        var props = Props.FromFunc(ctx =>
        {
            if (ctx.Message is ActivateStudent)
                ctx.Respond(new ActivateStudentResponse(true));
            return Task.CompletedTask;
        });
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        var response = await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));

        Assert.True(response.Success);

        await system.ShutdownAsync();
    }

    [Fact]
    public async Task ActivateStudent_DuplicateActivation_IsIdempotent()
    {
        var system = new ActorSystem();
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        var r1 = await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));
        var r2 = await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));

        Assert.True(r1.Success);
        Assert.True(r2.Success); // Idempotent

        await system.ShutdownAsync();
    }

    [Fact]
    public async Task StudentDeactivated_RemovesFromActivePool()
    {
        var system = new ActorSystem();
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        // Activate then deactivate
        await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));
        system.Root.Send(pid, new StudentDeactivated("student-1"));

        // Small delay for message processing
        await Task.Delay(100);

        // Get metrics — active count should be 0
        var metrics = await system.Root.RequestAsync<ManagerMetricsResponse>(
            pid, new GetManagerMetrics(), TimeSpan.FromSeconds(2));

        Assert.Equal(0, metrics.ActiveCount);

        await system.ShutdownAsync();
    }

    [Fact]
    public async Task GetManagerMetrics_ReturnsCorrectCounts()
    {
        var system = new ActorSystem();
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        // Activate 3 students
        for (int i = 1; i <= 3; i++)
        {
            await system.Root.RequestAsync<ActivateStudentResponse>(
                pid, new ActivateStudent($"student-{i}"), TimeSpan.FromSeconds(2));
        }

        var metrics = await system.Root.RequestAsync<ManagerMetricsResponse>(
            pid, new GetManagerMetrics(), TimeSpan.FromSeconds(2));

        Assert.Equal(3, metrics.ActiveCount);
        Assert.True(metrics.AcceptingNewActivations);

        await system.ShutdownAsync();
    }

    [Fact]
    public async Task StopNewActivations_RejectsSubsequentActivations()
    {
        var system = new ActorSystem();
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        // Activate one student, then stop accepting
        await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));
        system.Root.Send(pid, new StopNewActivations());
        await Task.Delay(50);

        // New activation should be rejected
        var response = await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-2"), TimeSpan.FromSeconds(2));

        Assert.False(response.Success);
        Assert.Equal("SHUTDOWN_IN_PROGRESS", response.ErrorCode);

        await system.ShutdownAsync();
    }

    [Fact]
    public async Task DrainAll_WaitsForDeactivations()
    {
        var system = new ActorSystem();
        var pid = system.Root.Spawn(Props.FromProducer(() => _manager));

        // Activate 2 students
        await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-1"), TimeSpan.FromSeconds(2));
        await system.Root.RequestAsync<ActivateStudentResponse>(
            pid, new ActivateStudent("student-2"), TimeSpan.FromSeconds(2));

        // Start drain (async) and then deactivate all students
        var drainTask = system.Root.RequestAsync<DrainAllResponse>(
            pid, new DrainAll(TimeSpan.FromSeconds(5)), TimeSpan.FromSeconds(10));

        // Simulate deactivations after short delay
        await Task.Delay(100);
        system.Root.Send(pid, new StudentDeactivated("student-1"));
        system.Root.Send(pid, new StudentDeactivated("student-2"));

        var drainResult = await drainTask;

        Assert.Equal(2, drainResult.DrainedCount);
        Assert.Equal(0, drainResult.RemainingCount);

        await system.ShutdownAsync();
    }
}
