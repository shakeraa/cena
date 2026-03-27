// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — OutreachSchedulerActor (Classic, Timer-Based)
// Manages HLR spaced repetition timers, streak warnings, and throttled
// outreach dispatch via NATS JetStream.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;
using Cena.Actors.Services;

namespace Cena.Actors.Outreach;

public sealed class OutreachSchedulerActor : IActor
{
    private readonly IHlrService _hlr;
    private readonly ILogger<OutreachSchedulerActor> _logger;

    // ── HLR Timer State: conceptId → (halfLifeHours, lastReviewAt) ──
    private readonly Dictionary<string, HlrTimerState> _timers = new();

    // ── Throttling ──
    private int _messagesSentToday;
    private DateTimeOffset _lastResetDate = DateTimeOffset.MinValue;
    private const int MaxMessagesPerDay = 3;

    // ── Quiet Hours (Israel time: UTC+2 or UTC+3 depending on DST) ──
    private static readonly TimeZoneInfo IsraelTz = ResolveIsraelTimeZone();

    private static TimeZoneInfo ResolveIsraelTimeZone()
    {
        // Linux/macOS use IANA IDs; Windows uses "Israel"
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem"); }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Israel");
        }
    }
    private const int QuietHourStart = 22; // 10 PM
    private const int QuietHourEnd = 7;     // 7 AM

    // ── Streak ──
    private DateTimeOffset _lastActivityDate;
    private int _currentStreak;
    private const int StreakWarningHoursBeforeExpiry = 4;

    // ── Outreach priorities ──
    private readonly SortedList<int, PendingOutreach> _pendingQueue = new();

    // ── Telemetry ──
    private static readonly Meter Meter = new("Cena.Actors.Outreach", "1.0.0");
    private static readonly Counter<long> OutreachSent =
        Meter.CreateCounter<long>("cena.outreach.sent_total");

    public OutreachSchedulerActor(IHlrService hlr, ILogger<OutreachSchedulerActor> logger)
    {
        _hlr = hlr;
        _logger = logger;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            ConceptMasteredNotification msg => HandleConceptMastered(msg),
            CheckHlrTimers => HandleCheckTimers(context),
            CheckStreakExpiry => HandleCheckStreak(context),
            UpdateActivity msg => HandleUpdateActivity(msg),
            UpdateContactPrefs msg => HandleContactPrefs(msg),
            _ => Task.CompletedTask
        };
    }

    // ── When a concept is mastered, start tracking its HLR timer ──
    private Task HandleConceptMastered(ConceptMasteredNotification msg)
    {
        _timers[msg.ConceptId] = new HlrTimerState(
            HalfLifeHours: msg.InitialHalfLifeHours,
            LastReviewAt: DateTimeOffset.UtcNow
        );

        _logger.LogDebug(
            "HLR timer started for concept {ConceptId}, half-life={HalfLife}h",
            msg.ConceptId, msg.InitialHalfLifeHours);

        return Task.CompletedTask;
    }

    // ── Periodic check: which concepts need review? ──
    private Task HandleCheckTimers(IContext context)
    {
        ResetDailyThrottleIfNeeded();

        foreach (var (conceptId, timer) in _timers)
        {
            double hoursSinceReview = (DateTimeOffset.UtcNow - timer.LastReviewAt).TotalHours;
            double predictedRecall = _hlr.ComputeRecall(timer.HalfLifeHours, hoursSinceReview);

            if (predictedRecall < 0.85)
            {
                EnqueueOutreach(new PendingOutreach(
                    Priority: 2, // ReviewDue priority
                    Type: "ReviewDue",
                    ConceptId: conceptId,
                    Message: $"Time to review: predicted recall {predictedRecall:P0}"
                ));
            }
        }

        // Dispatch queued messages (respects throttle + quiet hours)
        DispatchPendingOutreach(context);

        return Task.CompletedTask;
    }

    // ── Streak check ──
    private Task HandleCheckStreak(IContext context)
    {
        if (_currentStreak <= 0) return Task.CompletedTask;

        var hoursSinceActivity = (DateTimeOffset.UtcNow - _lastActivityDate).TotalHours;
        var hoursUntilExpiry = 24.0 - hoursSinceActivity;

        if (hoursUntilExpiry <= StreakWarningHoursBeforeExpiry && hoursUntilExpiry > 0)
        {
            EnqueueOutreach(new PendingOutreach(
                Priority: 1, // StreakExpiring — highest priority
                Type: "StreakExpiring",
                ConceptId: null,
                Message: $"Your {_currentStreak}-day streak expires in {hoursUntilExpiry:F0} hours!"
            ));
            DispatchPendingOutreach(context);
        }

        return Task.CompletedTask;
    }

    private Task HandleUpdateActivity(UpdateActivity msg)
    {
        _lastActivityDate = msg.ActivityDate;
        _currentStreak = msg.CurrentStreak;

        // Update HLR timer if this was a review of a tracked concept
        if (msg.ReviewedConceptId != null && _timers.ContainsKey(msg.ReviewedConceptId))
        {
            var timer = _timers[msg.ReviewedConceptId];
            double newHalfLife = _hlr.UpdateHalfLife(
                timer.HalfLifeHours, msg.WasCorrect, (int)msg.ResponseTimeMs);

            _timers[msg.ReviewedConceptId] = timer with
            {
                HalfLifeHours = newHalfLife,
                LastReviewAt = DateTimeOffset.UtcNow
            };
        }

        return Task.CompletedTask;
    }

    private Task HandleContactPrefs(UpdateContactPrefs msg)
    {
        // Store preferences for channel routing (future: persist to state)
        _logger.LogDebug("Contact preferences updated: channel={Channel}", msg.PreferredChannel);
        return Task.CompletedTask;
    }

    // ── Throttle + Quiet Hours + Dispatch ──

    // Monotonic counter for unique SortedList keys (prevents key collision after dispatch)
    private int _enqueueCounter;

    private void EnqueueOutreach(PendingOutreach outreach)
    {
        // Use priority * large factor + monotonic counter for unique, priority-ordered keys
        var key = outreach.Priority * 100_000 + _enqueueCounter++;
        _pendingQueue[key] = outreach;
    }

    private void DispatchPendingOutreach(IContext context)
    {
        ResetDailyThrottleIfNeeded();

        if (IsQuietHours())
        {
            _logger.LogDebug("Quiet hours active — deferring {Count} outreach messages", _pendingQueue.Count);
            return;
        }

        while (_pendingQueue.Count > 0 && _messagesSentToday < MaxMessagesPerDay)
        {
            var (_, outreach) = _pendingQueue.First();
            _pendingQueue.RemoveAt(0);

            // Publish to NATS for outreach service to dispatch
            context.Send(context.Parent!, new OutreachDispatchRequest(
                Type: outreach.Type,
                ConceptId: outreach.ConceptId,
                Message: outreach.Message,
                Priority: outreach.Priority
            ));

            _messagesSentToday++;
            OutreachSent.Add(1, new KeyValuePair<string, object?>("type", outreach.Type));

            _logger.LogInformation(
                "Outreach dispatched: type={Type}, priority={Priority}, today={Count}/{Max}",
                outreach.Type, outreach.Priority, _messagesSentToday, MaxMessagesPerDay);
        }

        if (_messagesSentToday >= MaxMessagesPerDay && _pendingQueue.Count > 0)
        {
            _logger.LogInformation(
                "Daily throttle reached ({Max}/day). {Remaining} messages deferred to tomorrow.",
                MaxMessagesPerDay, _pendingQueue.Count);
        }
    }

    private bool IsQuietHours()
    {
        var israelNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IsraelTz);
        var hour = israelNow.Hour;
        return hour >= QuietHourStart || hour < QuietHourEnd;
    }

    private void ResetDailyThrottleIfNeeded()
    {
        var today = DateTimeOffset.UtcNow.Date;
        if (_lastResetDate.Date != today)
        {
            _messagesSentToday = 0;
            _lastResetDate = DateTimeOffset.UtcNow;
        }
    }
}

// ── State ──

internal record HlrTimerState(double HalfLifeHours, DateTimeOffset LastReviewAt);

internal record PendingOutreach(int Priority, string Type, string? ConceptId, string Message);

// ── Messages ──

public record ConceptMasteredNotification(string ConceptId, double InitialHalfLifeHours);
public record CheckHlrTimers;
public record CheckStreakExpiry;
public record UpdateActivity(
    DateTimeOffset ActivityDate, int CurrentStreak,
    string? ReviewedConceptId, bool WasCorrect, double ResponseTimeMs);
public record UpdateContactPrefs(string PreferredChannel);
public record OutreachDispatchRequest(string Type, string? ConceptId, string Message, int Priority);
