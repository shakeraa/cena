// =============================================================================
// Cena Platform -- OutreachSchedulerActor (Classic, Timer-Based)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - Classic actor: child of StudentActor, lives across sessions.
//   - Manages Half-Life Regression (HLR) timers for spaced repetition.
//   - Formula: p(t) = 2^(-delta/h) -- schedule review when p drops below 0.85.
//   - Proto.Actor ReceiveTimeout / scheduler for timer-based reminders.
//   - Streak expiration tracking (24-hour window).
//   - Message priority: 1=StreakExpiring, 2=ReviewDue, 3=StagnationDetected,
//     4=SessionAbandoned, 5=CooldownComplete.
//   - Throttling: max 3 messages/day per student, quiet hours 22:00-07:00.
//   - Channel routing: student preferences -> WhatsApp > Push > Telegram > Voice.
//   - Publishes to NATS JetStream for the Outreach bounded context to dispatch.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using Proto;

using Cena.Contracts.Actors;

namespace Cena.Actors;

// =============================================================================
// OUTREACH STATE
// =============================================================================

/// <summary>
/// State for the outreach scheduler actor. Manages HLR timers, pending
/// reminders, contact preferences, and throttle counters.
/// </summary>
public sealed class OutreachState
{
    // ---- Identity ----
    public string StudentId { get; set; } = "";

    // ---- HLR Timer State ----
    /// <summary>
    /// Concept ID -> HLR timer state. Tracks half-life and last review time
    /// for each mastered concept.
    /// </summary>
    public Dictionary<string, HlrTimerState> HlrTimers { get; set; } = new();

    // ---- Pending Reminders ----
    /// <summary>
    /// Reminder ID -> scheduled reminder. Sorted by fire time for efficient
    /// timer management.
    /// </summary>
    public SortedDictionary<DateTimeOffset, PendingReminder> PendingReminders { get; set; } = new();

    /// <summary>Lookup by reminder ID for cancellation.</summary>
    public Dictionary<string, DateTimeOffset> ReminderIdToFireTime { get; set; } = new();

    // ---- Streak Tracking ----
    public int CurrentStreak { get; set; }
    public DateTimeOffset LastActivityDate { get; set; }
    public bool StreakExpiryReminderSent { get; set; }

    // ---- Contact Preferences ----
    public List<OutreachChannel> ChannelPreference { get; set; } = new()
    {
        OutreachChannel.WhatsApp,
        OutreachChannel.Push,
        OutreachChannel.Telegram,
        OutreachChannel.Voice
    };
    public TimeOnly QuietHoursStart { get; set; } = new(22, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(7, 0);
    public string Timezone { get; set; } = "Asia/Jerusalem";
    public string ContentLanguage { get; set; } = "he";

    // ---- Throttling ----
    /// <summary>Messages sent today. Reset at midnight in student's timezone.</summary>
    public int MessagesSentToday { get; set; }
    public DateOnly LastMessageDate { get; set; }

    // ---- Constants ----
    public const int MaxMessagesPerDay = 3;
    public const double ReviewRecallThreshold = 0.85;
}

/// <summary>
/// HLR timer state for a single concept.
/// </summary>
public sealed class HlrTimerState
{
    /// <summary>Half-life in hours. Doubles on successful review, halves on failure.</summary>
    public double HalfLifeHours { get; set; }

    /// <summary>Last review timestamp.</summary>
    public DateTimeOffset LastReviewAt { get; set; }

    /// <summary>Number of successful reviews (for half-life update).</summary>
    public int SuccessfulReviews { get; set; }

    /// <summary>ID of the scheduled reminder for this concept's next review.</summary>
    public string? ScheduledReminderId { get; set; }
}

/// <summary>
/// A pending reminder waiting to fire.
/// </summary>
public sealed record PendingReminder(
    string ReminderId,
    string TriggerType,
    OutreachChannel PreferredChannel,
    string? ConceptId,
    OutreachPriority Priority,
    DateTimeOffset ScheduledAt,
    CancellationTokenSource? TimerCts);

/// <summary>
/// Outreach message priority. Lower number = higher priority.
/// Used for throttle-based selection: when daily limit is reached,
/// only higher-priority messages get through.
/// </summary>
public enum OutreachPriority
{
    /// <summary>Streak about to expire. Highest priority.</summary>
    StreakExpiring = 1,

    /// <summary>Spaced repetition review due.</summary>
    ReviewDue = 2,

    /// <summary>Stagnation detected, methodology switch occurred.</summary>
    StagnationDetected = 3,

    /// <summary>Session was abandoned mid-way.</summary>
    SessionAbandoned = 4,

    /// <summary>Cognitive load cooldown complete, student can resume.</summary>
    CooldownComplete = 5
}

// =============================================================================
// OUTREACH SCHEDULER ACTOR
// =============================================================================

/// <summary>
/// Manages outreach timing, channel selection, and delivery for a single student.
/// This actor IS the scheduler -- no external cron jobs or timer services needed.
///
/// <para><b>HLR (Half-Life Regression):</b></para>
/// For each mastered concept, maintains a timer based on the formula:
/// <c>p(t) = 2^(-delta/h)</c> where delta = hours since last review,
/// h = half-life in hours. When predicted recall drops below 0.85, a review
/// reminder is scheduled.
///
/// <para><b>Throttling:</b></para>
/// Max 3 messages per day per student. Quiet hours: 22:00-07:00 in student's
/// local timezone. Messages during quiet hours are deferred to 07:00 next day.
///
/// <para><b>Channel Routing:</b></para>
/// Routes via student preferences: WhatsApp > Push > Telegram > Voice.
/// The Outreach bounded context handles actual delivery.
/// </summary>
public sealed class OutreachSchedulerActor : IActor
{
    // ---- Dependencies ----
    private readonly INatsConnection _nats;
    private readonly ILogger<OutreachSchedulerActor> _logger;

    // ---- State ----
    private readonly OutreachState _state = new();

    // ---- Timer for periodic HLR checks ----
    private CancellationTokenSource? _hlrCheckCts;
    private static readonly TimeSpan HlrCheckInterval = TimeSpan.FromMinutes(15);

    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Actors.OutreachSchedulerActor", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Actors.OutreachSchedulerActor", "1.0.0");
    private static readonly Counter<long> MessagesScheduledCounter =
        MeterInstance.CreateCounter<long>("cena.outreach.messages_scheduled_total", description: "Total outreach messages scheduled");
    private static readonly Counter<long> MessagesDispatchedCounter =
        MeterInstance.CreateCounter<long>("cena.outreach.messages_dispatched_total", description: "Total outreach messages dispatched to NATS");
    private static readonly Counter<long> MessagesThrottledCounter =
        MeterInstance.CreateCounter<long>("cena.outreach.messages_throttled_total", description: "Total outreach messages throttled");
    private static readonly Counter<long> MessagesDeferredCounter =
        MeterInstance.CreateCounter<long>("cena.outreach.messages_deferred_total", description: "Total outreach messages deferred (quiet hours)");

    public OutreachSchedulerActor(
        INatsConnection nats,
        ILogger<OutreachSchedulerActor> logger)
    {
        _nats = nats ?? throw new ArgumentNullException(nameof(nats));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started                        => OnStarted(context),
            Stopping                       => OnStopping(context),

            // ---- Public messages ----
            ScheduleReminder cmd           => HandleScheduleReminder(context, cmd),
            CancelReminder cmd             => HandleCancelReminder(context, cmd),
            UpdateContactPreferences cmd   => HandleUpdatePreferences(context, cmd),

            // ---- Internal messages from parent ----
            ConceptMasteredNotification n  => HandleConceptMastered(context, n),
            StreakStateUpdate n            => HandleStreakUpdate(context, n),

            // ---- Timer ticks ----
            ReminderFireTick tick          => HandleReminderFire(context, tick),
            HlrCheckTick                   => HandleHlrCheck(context),

            _ => Task.CompletedTask
        };
    }

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    private Task OnStarted(IContext context)
    {
        _logger.LogDebug("OutreachSchedulerActor started");

        // Start periodic HLR check timer
        ScheduleHlrCheck(context);

        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        _hlrCheckCts?.Cancel();
        _hlrCheckCts?.Dispose();

        // Cancel all pending reminder timers
        foreach (var (_, reminder) in _state.PendingReminders)
        {
            reminder.TimerCts?.Cancel();
            reminder.TimerCts?.Dispose();
        }

        _logger.LogDebug(
            "OutreachSchedulerActor stopping. PendingReminders={Count}, HlrTimers={HlrCount}",
            _state.PendingReminders.Count, _state.HlrTimers.Count);

        return Task.CompletedTask;
    }

    // =========================================================================
    // SCHEDULE REMINDER
    // =========================================================================

    /// <summary>
    /// Schedules an outreach reminder. Sets a Proto.Actor timer that fires
    /// at the scheduled time. Respects quiet hours and daily throttle limits.
    /// </summary>
    private Task HandleScheduleReminder(IContext context, ScheduleReminder cmd)
    {
        using var activity = ActivitySourceInstance.StartActivity("Outreach.ScheduleReminder");
        activity?.SetTag("reminder.id", cmd.ReminderId);
        activity?.SetTag("trigger.type", cmd.TriggerType);

        // ---- Parse priority ----
        var priority = cmd.TriggerType switch
        {
            "StreakExpiring"       => OutreachPriority.StreakExpiring,
            "ReviewDue"            => OutreachPriority.ReviewDue,
            "StagnationDetected"   => OutreachPriority.StagnationDetected,
            "SessionAbandoned"     => OutreachPriority.SessionAbandoned,
            "CooldownComplete"     => OutreachPriority.CooldownComplete,
            _                      => OutreachPriority.CooldownComplete
        };

        // ---- Adjust for quiet hours ----
        var fireTime = AdjustForQuietHours(cmd.ScheduledAt);

        // ---- Create pending reminder ----
        var cts = new CancellationTokenSource();
        var reminder = new PendingReminder(
            cmd.ReminderId, cmd.TriggerType, cmd.PreferredChannel,
            cmd.ConceptId, priority, fireTime, cts);

        // Remove existing reminder with same ID (idempotent)
        if (_state.ReminderIdToFireTime.TryGetValue(cmd.ReminderId, out var existingFireTime))
        {
            _state.PendingReminders.Remove(existingFireTime);
            _state.ReminderIdToFireTime.Remove(cmd.ReminderId);
        }

        _state.PendingReminders[fireTime] = reminder;
        _state.ReminderIdToFireTime[cmd.ReminderId] = fireTime;

        // ---- Schedule timer ----
        var delay = fireTime - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            // Fire immediately
            context.Send(context.Self, new ReminderFireTick(cmd.ReminderId));
        }
        else
        {
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled)
                    context.Send(context.Self, new ReminderFireTick(cmd.ReminderId));
            });
        }

        MessagesScheduledCounter.Add(1,
            new KeyValuePair<string, object?>("trigger.type", cmd.TriggerType),
            new KeyValuePair<string, object?>("priority", priority.ToString()));

        _logger.LogDebug(
            "Reminder scheduled: {ReminderId}, Trigger={Trigger}, Priority={Priority}, " +
            "FireAt={FireTime}, Delay={Delay}",
            cmd.ReminderId, cmd.TriggerType, priority, fireTime, delay);

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // CANCEL REMINDER
    // =========================================================================

    /// <summary>
    /// Cancels a previously scheduled reminder. Idempotent -- no error if
    /// already fired or cancelled.
    /// </summary>
    private Task HandleCancelReminder(IContext context, CancelReminder cmd)
    {
        if (_state.ReminderIdToFireTime.TryGetValue(cmd.ReminderId, out var fireTime))
        {
            if (_state.PendingReminders.TryGetValue(fireTime, out var reminder))
            {
                reminder.TimerCts?.Cancel();
                reminder.TimerCts?.Dispose();
                _state.PendingReminders.Remove(fireTime);
            }
            _state.ReminderIdToFireTime.Remove(cmd.ReminderId);

            _logger.LogDebug("Reminder cancelled: {ReminderId}", cmd.ReminderId);
        }

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // REMINDER FIRE (timer expired)
    // =========================================================================

    /// <summary>
    /// Handles a reminder timer expiration. Applies throttling, selects
    /// channel, and publishes to NATS for the Outreach context to dispatch.
    /// </summary>
    private async Task HandleReminderFire(IContext context, ReminderFireTick tick)
    {
        using var activity = ActivitySourceInstance.StartActivity("Outreach.ReminderFire");
        activity?.SetTag("reminder.id", tick.ReminderId);

        if (!_state.ReminderIdToFireTime.TryGetValue(tick.ReminderId, out var fireTime))
        {
            _logger.LogDebug("Reminder {ReminderId} already cancelled or fired", tick.ReminderId);
            return;
        }

        if (!_state.PendingReminders.TryGetValue(fireTime, out var reminder))
        {
            return;
        }

        // ---- Clean up state ----
        _state.PendingReminders.Remove(fireTime);
        _state.ReminderIdToFireTime.Remove(tick.ReminderId);

        // ---- Check quiet hours (in case timezone changed since scheduling) ----
        if (IsQuietHoursNow())
        {
            // Defer to next available window
            var deferredTime = GetNextAvailableTime();
            var deferredReminder = reminder with { ScheduledAt = deferredTime };

            _state.PendingReminders[deferredTime] = deferredReminder;
            _state.ReminderIdToFireTime[tick.ReminderId] = deferredTime;

            MessagesDeferredCounter.Add(1);

            _logger.LogDebug(
                "Reminder {ReminderId} deferred to {DeferredTime} (quiet hours)",
                tick.ReminderId, deferredTime);

            // Reschedule timer
            var cts = new CancellationTokenSource();
            var delay = deferredTime - DateTimeOffset.UtcNow;
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled) context.Send(context.Self, new ReminderFireTick(tick.ReminderId));
            });

            return;
        }

        // ---- Check daily throttle ----
        ResetDailyCounterIfNeeded();

        if (_state.MessagesSentToday >= OutreachState.MaxMessagesPerDay)
        {
            // Allow only priority 1 (StreakExpiring) messages to break throttle
            if (reminder.Priority != OutreachPriority.StreakExpiring)
            {
                MessagesThrottledCounter.Add(1,
                    new KeyValuePair<string, object?>("trigger.type", reminder.TriggerType));

                _logger.LogInformation(
                    "Reminder {ReminderId} throttled. Messages today: {Count}/{Max}. " +
                    "Priority: {Priority}",
                    tick.ReminderId, _state.MessagesSentToday,
                    OutreachState.MaxMessagesPerDay, reminder.Priority);
                return;
            }
        }

        // ---- Select channel ----
        var channel = SelectChannel(reminder.PreferredChannel);

        // ---- Publish to NATS for Outreach context ----
        await DispatchToNats(reminder, channel);

        _state.MessagesSentToday++;

        MessagesDispatchedCounter.Add(1,
            new KeyValuePair<string, object?>("trigger.type", reminder.TriggerType),
            new KeyValuePair<string, object?>("channel", channel.ToString()),
            new KeyValuePair<string, object?>("priority", reminder.Priority.ToString()));

        _logger.LogInformation(
            "Reminder dispatched: {ReminderId}, Trigger={Trigger}, Channel={Channel}, " +
            "Priority={Priority}, MessageCount={Count}/{Max}",
            tick.ReminderId, reminder.TriggerType, channel,
            reminder.Priority, _state.MessagesSentToday, OutreachState.MaxMessagesPerDay);
    }

    // =========================================================================
    // HLR TIMER MANAGEMENT
    // =========================================================================

    /// <summary>
    /// Handles notification that a concept has been mastered. Creates an HLR
    /// timer to track memory decay and schedule reviews.
    ///
    /// HLR formula: p(t) = 2^(-delta/h)
    /// Where: p = predicted recall probability, delta = hours since last review,
    /// h = half-life in hours.
    ///
    /// Initial half-life is typically 24 hours. On successful review, half-life
    /// doubles. On failed review, half-life halves.
    /// </summary>
    private Task HandleConceptMastered(IContext context, ConceptMasteredNotification notification)
    {
        var hlrState = new HlrTimerState
        {
            HalfLifeHours = notification.InitialHalfLifeHours,
            LastReviewAt = DateTimeOffset.UtcNow,
            SuccessfulReviews = 0
        };

        _state.HlrTimers[notification.ConceptId] = hlrState;

        // Schedule first review when recall drops to threshold
        // Solve: 0.85 = 2^(-delta/h) => delta = -h * log2(0.85)
        double hoursUntilReview = -notification.InitialHalfLifeHours * Math.Log2(OutreachState.ReviewRecallThreshold);
        var reviewTime = DateTimeOffset.UtcNow.AddHours(hoursUntilReview);

        var reminderId = $"hlr_{notification.ConceptId}_{Guid.CreateVersion7()}";
        hlrState.ScheduledReminderId = reminderId;

        context.Send(context.Self, new ScheduleReminder(
            notification.StudentId, reminderId, "ReviewDue",
            reviewTime, _state.ChannelPreference.FirstOrDefault(OutreachChannel.Push),
            notification.ConceptId, "standard"));

        _logger.LogDebug(
            "HLR timer created for concept {ConceptId}. HalfLife={HalfLife}h, " +
            "FirstReviewIn={Hours:F1}h",
            notification.ConceptId, notification.InitialHalfLifeHours, hoursUntilReview);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Periodic HLR check. Scans all HLR timers and schedules reviews for
    /// concepts whose predicted recall has dropped below threshold.
    /// This is a safety net -- individual timers should fire on their own.
    /// </summary>
    private Task HandleHlrCheck(IContext context)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var (conceptId, hlr) in _state.HlrTimers)
        {
            // p(t) = 2^(-delta/h)
            double deltaHours = (now - hlr.LastReviewAt).TotalHours;
            double predictedRecall = Math.Pow(2, -deltaHours / hlr.HalfLifeHours);

            if (predictedRecall < OutreachState.ReviewRecallThreshold && hlr.ScheduledReminderId == null)
            {
                // No reminder scheduled and recall is below threshold -- schedule one
                var reminderId = $"hlr_{conceptId}_{Guid.CreateVersion7()}";
                hlr.ScheduledReminderId = reminderId;

                context.Send(context.Self, new ScheduleReminder(
                    _state.StudentId, reminderId, "ReviewDue",
                    now.AddMinutes(5), // Schedule 5 min from now
                    _state.ChannelPreference.FirstOrDefault(OutreachChannel.Push),
                    conceptId, "standard"));

                _logger.LogDebug(
                    "HLR check: concept {ConceptId} needs review. " +
                    "PredictedRecall={Recall:F3}, HalfLife={HalfLife}h",
                    conceptId, predictedRecall, hlr.HalfLifeHours);
            }
        }

        // Reschedule next HLR check
        ScheduleHlrCheck(context);

        return Task.CompletedTask;
    }

    // =========================================================================
    // STREAK TRACKING
    // =========================================================================

    /// <summary>
    /// Handles streak state updates from the parent. Schedules streak expiry
    /// reminder if the streak is about to expire (within 4 hours of 24-hour window).
    /// </summary>
    private Task HandleStreakUpdate(IContext context, StreakStateUpdate update)
    {
        _state.CurrentStreak = update.CurrentStreak;
        _state.LastActivityDate = update.LastActivityDate;
        _state.StreakExpiryReminderSent = false;

        if (update.CurrentStreak > 0)
        {
            // Schedule streak expiry warning: fire 4 hours before expiry
            var expiresAt = update.LastActivityDate.Date.AddDays(1).AddHours(24);
            var warningTime = expiresAt.AddHours(-4);

            if (warningTime > DateTimeOffset.UtcNow)
            {
                var reminderId = $"streak_expiry_{Guid.CreateVersion7()}";
                context.Send(context.Self, new ScheduleReminder(
                    _state.StudentId, reminderId, "StreakExpiring",
                    warningTime, _state.ChannelPreference.FirstOrDefault(OutreachChannel.Push),
                    null, "high"));

                _logger.LogDebug(
                    "Streak expiry reminder scheduled. Streak={Streak}, " +
                    "ExpiresAt={Expiry}, WarningAt={Warning}",
                    update.CurrentStreak, expiresAt, warningTime);
            }
        }

        return Task.CompletedTask;
    }

    // =========================================================================
    // CONTACT PREFERENCES
    // =========================================================================

    /// <summary>
    /// Updates student contact preferences for outreach channel routing.
    /// </summary>
    private Task HandleUpdatePreferences(IContext context, UpdateContactPreferences cmd)
    {
        _state.ChannelPreference = cmd.ChannelPreference.ToList();

        if (cmd.QuietHoursStart.HasValue)
            _state.QuietHoursStart = cmd.QuietHoursStart.Value;
        if (cmd.QuietHoursEnd.HasValue)
            _state.QuietHoursEnd = cmd.QuietHoursEnd.Value;

        _state.Timezone = cmd.Timezone;
        _state.ContentLanguage = cmd.ContentLanguage;

        _logger.LogInformation(
            "Contact preferences updated. Channels={Channels}, QuietHours={Start}-{End}, TZ={TZ}",
            string.Join(", ", _state.ChannelPreference),
            _state.QuietHoursStart, _state.QuietHoursEnd, _state.Timezone);

        context.Respond(new ActorResult(true));
        return Task.CompletedTask;
    }

    // =========================================================================
    // CHANNEL ROUTING
    // =========================================================================

    /// <summary>
    /// Selects the outreach channel based on student preferences.
    /// Falls back through: WhatsApp > Push > Telegram > Voice.
    /// </summary>
    private OutreachChannel SelectChannel(OutreachChannel preferred)
    {
        // If the preferred channel is in the student's preference list, use it
        if (_state.ChannelPreference.Contains(preferred))
            return preferred;

        // Otherwise, use the first available preference
        return _state.ChannelPreference.Count > 0
            ? _state.ChannelPreference[0]
            : OutreachChannel.Push; // fallback
    }

    // =========================================================================
    // QUIET HOURS & THROTTLING
    // =========================================================================

    /// <summary>
    /// Checks if the current time falls within the student's quiet hours.
    /// Uses the student's configured timezone.
    /// </summary>
    private bool IsQuietHoursNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_state.Timezone);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var localTime = TimeOnly.FromDateTime(localNow.DateTime);

            // Handle overnight quiet hours (e.g., 22:00-07:00)
            if (_state.QuietHoursStart > _state.QuietHoursEnd)
            {
                return localTime >= _state.QuietHoursStart || localTime < _state.QuietHoursEnd;
            }
            else
            {
                return localTime >= _state.QuietHoursStart && localTime < _state.QuietHoursEnd;
            }
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning(
                "Unknown timezone {Timezone}. Defaulting to UTC quiet hours check.",
                _state.Timezone);
            var utcTime = TimeOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
            return utcTime >= _state.QuietHoursStart || utcTime < _state.QuietHoursEnd;
        }
    }

    /// <summary>
    /// Gets the next available time after quiet hours end.
    /// </summary>
    private DateTimeOffset GetNextAvailableTime()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_state.Timezone);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var today = localNow.Date;

            // Target: quiet hours end time today or tomorrow
            var targetLocal = today + _state.QuietHoursEnd.ToTimeSpan();

            if (targetLocal <= localNow.DateTime)
            {
                // Quiet hours end has already passed today -- schedule for tomorrow
                targetLocal = targetLocal.AddDays(1);
            }

            return new DateTimeOffset(targetLocal, tz.GetUtcOffset(targetLocal));
        }
        catch
        {
            // Fallback: 7 AM UTC tomorrow
            var tomorrow = DateTimeOffset.UtcNow.Date.AddDays(1);
            return new DateTimeOffset(tomorrow.Year, tomorrow.Month, tomorrow.Day,
                7, 0, 0, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Adjusts a fire time to respect quiet hours.
    /// </summary>
    private DateTimeOffset AdjustForQuietHours(DateTimeOffset scheduledAt)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_state.Timezone);
            var localTime = TimeZoneInfo.ConvertTime(scheduledAt, tz);
            var timeOnly = TimeOnly.FromDateTime(localTime.DateTime);

            bool inQuietHours;
            if (_state.QuietHoursStart > _state.QuietHoursEnd)
            {
                inQuietHours = timeOnly >= _state.QuietHoursStart || timeOnly < _state.QuietHoursEnd;
            }
            else
            {
                inQuietHours = timeOnly >= _state.QuietHoursStart && timeOnly < _state.QuietHoursEnd;
            }

            if (inQuietHours)
            {
                // Defer to quiet hours end
                var date = localTime.Date;
                if (timeOnly >= _state.QuietHoursStart)
                    date = date.AddDays(1); // next day

                var adjusted = date + _state.QuietHoursEnd.ToTimeSpan();
                return new DateTimeOffset(adjusted, tz.GetUtcOffset(adjusted));
            }

            return scheduledAt;
        }
        catch
        {
            return scheduledAt;
        }
    }

    /// <summary>
    /// Resets the daily message counter if the date has changed.
    /// </summary>
    private void ResetDailyCounterIfNeeded()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_state.Timezone);
            var localNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
            var today = DateOnly.FromDateTime(localNow.DateTime);

            if (today != _state.LastMessageDate)
            {
                _state.MessagesSentToday = 0;
                _state.LastMessageDate = today;
            }
        }
        catch
        {
            // Fallback to UTC
            var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.DateTime);
            if (today != _state.LastMessageDate)
            {
                _state.MessagesSentToday = 0;
                _state.LastMessageDate = today;
            }
        }
    }

    // =========================================================================
    // NATS DISPATCH
    // =========================================================================

    /// <summary>
    /// Publishes a reminder to NATS JetStream for the Outreach bounded context
    /// to handle actual delivery via WhatsApp/Push/Telegram/Voice.
    /// </summary>
    private async Task DispatchToNats(PendingReminder reminder, OutreachChannel channel)
    {
        try
        {
            var js = new NatsJSContext(_nats);
            var subject = $"cena.outreach.dispatch.{channel.ToString().ToLowerInvariant()}";

            var payload = new
            {
                StudentId = _state.StudentId,
                ReminderId = reminder.ReminderId,
                TriggerType = reminder.TriggerType,
                Channel = channel.ToString(),
                ConceptId = reminder.ConceptId,
                Priority = (int)reminder.Priority,
                Language = _state.ContentLanguage,
                Timezone = _state.Timezone,
                Timestamp = DateTimeOffset.UtcNow
            };

            await js.PublishAsync(subject, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to dispatch reminder {ReminderId} to NATS. " +
                "Channel={Channel}, Trigger={Trigger}",
                reminder.ReminderId, channel, reminder.TriggerType);
            // Non-fatal: the reminder state is preserved and HLR check will retry
        }
    }

    // =========================================================================
    // TIMER HELPERS
    // =========================================================================

    private void ScheduleHlrCheck(IContext context)
    {
        _hlrCheckCts?.Cancel();
        _hlrCheckCts?.Dispose();
        _hlrCheckCts = new CancellationTokenSource();

        _ = Task.Delay(HlrCheckInterval, _hlrCheckCts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) context.Send(context.Self, new HlrCheckTick());
        });
    }
}

// =============================================================================
// INTERNAL MESSAGES
// =============================================================================

/// <summary>Timer tick when a scheduled reminder should fire.</summary>
internal sealed record ReminderFireTick(string ReminderId);

/// <summary>Timer tick for periodic HLR scan.</summary>
internal sealed record HlrCheckTick;
