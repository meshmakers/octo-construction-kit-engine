using System.Globalization;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.AuditTrails;

/// <summary>
/// Decorator of an <see cref="IAuditEventSink"/> that summarises repeated facts (AB#7279). Events
/// with an <see cref="AuditEvent.AggregationKey"/> are aggregated per (<see cref="AuditEvent.TenantId"/>,
/// key):
/// <list type="bullet">
/// <item>the first occurrence is published to the inner sink at once, unchanged;</item>
/// <item>repetitions within <see cref="AuditEventAggregationOptions.Window"/> are only counted;</item>
/// <item>when the window ends, one summary with the count and the first/last time of the repetitions is
/// published, and a new window starts for the key. A key without repetitions in its window is dropped,
/// so its next occurrence is published at once again.</item>
/// </list>
/// Events without a key are passed through one by one. The key store is bounded
/// (<see cref="AuditEventAggregationOptions.MaxKeys"/>; eviction publishes the pending summary), and
/// <see cref="DisposeAsync"/> publishes every pending summary, so no count is lost on shutdown.
/// </summary>
/// <remarks>
/// The engine registers this sink as its own singleton around the host's <see cref="IAuditEventSink"/>
/// (log or event store) and hands it to the publishers of repeated facts (the data-permission checks);
/// it is deliberately not registered as <see cref="IAuditEventSink"/> itself, so hosts keep their sink
/// type (e.g. a strict sink of octo-common-services) for every other audit event.
/// </remarks>
public sealed class AggregatingAuditEventSink : IAuditEventSink, IAsyncDisposable, IDisposable
{
    private readonly IAuditEventSink _inner;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _window;
    private readonly int _maxKeys;
    private readonly bool _enabled;
    private readonly Dictionary<AggregationKey, Entry> _entries = new();
    private readonly Lock _lock = new();
    private readonly ITimer? _timer;
    private int _flushRunning;
    private Task _timerFlush = Task.CompletedTask;
    private bool _disposed;

    /// <summary>Constructor.</summary>
    /// <param name="inner">The sink that receives first occurrences, summaries and all other events.</param>
    /// <param name="options">Aggregation options.</param>
    /// <param name="logger">Logger for failures of the periodic flush; optional.</param>
    /// <param name="timeProvider">Clock and timer; <see cref="TimeProvider.System"/> when omitted.</param>
    public AggregatingAuditEventSink(IAuditEventSink inner, IOptions<AuditEventAggregationOptions> options,
        ILogger<AggregatingAuditEventSink>? logger = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        _inner = inner;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        var value = options.Value;
        _window = value.Window;
        _maxKeys = Math.Max(1, value.MaxKeys);
        _enabled = value.Enabled && _window > TimeSpan.Zero;
        if (_enabled)
        {
            // Check often enough that a summary is at most a small fraction of the window late.
            var tick = TimeSpan.FromTicks(Math.Clamp(_window.Ticks / 15, TimeSpan.TicksPerSecond,
                TimeSpan.TicksPerMinute));
            _timer = _timeProvider.CreateTimer(_ => OnTimer(), null, tick, tick);
        }
    }

    /// <summary>Number of keys currently held (for tests and diagnostics).</summary>
    internal int KeyCount
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (!_enabled || auditEvent.AggregationKey is null)
        {
            await _inner.PublishAsync(auditEvent, cancellationToken).ConfigureAwait(false);
            return;
        }

        var key = new AggregationKey(auditEvent.TenantId ?? string.Empty, auditEvent.AggregationKey);
        var now = _timeProvider.GetUtcNow();
        AuditEvent? evictedSummary = null;
        bool publishNow;
        lock (_lock)
        {
            if (_disposed)
            {
                publishNow = true;
            }
            else if (_entries.TryGetValue(key, out var entry))
            {
                entry.Count(now);
                publishNow = false;
            }
            else
            {
                if (_entries.Count >= _maxKeys)
                {
                    evictedSummary = EvictLeastRecentlySeen();
                }

                _entries[key] = new Entry(auditEvent, now);
                publishNow = true;
            }
        }

        if (evictedSummary is not null)
        {
            await PublishSummaryAsync(evictedSummary).ConfigureAwait(false);
        }

        if (publishNow)
        {
            await _inner.PublishAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes the summaries of every key whose window has ended and starts a new window for keys with
    /// repetitions; keys without repetitions are dropped. Called by the internal timer.
    /// </summary>
    internal async Task FlushDueAsync()
    {
        var now = _timeProvider.GetUtcNow();
        var summaries = new List<AuditEvent>();
        lock (_lock)
        {
            var ended = new List<AggregationKey>();
            foreach (var (key, entry) in _entries)
            {
                if (now - entry.WindowStart < _window)
                {
                    continue;
                }

                if (entry.Repetitions > 0)
                {
                    summaries.Add(entry.BuildSummary(_window));
                    entry.StartNewWindow(now);
                }
                else
                {
                    ended.Add(key);
                }
            }

            foreach (var key in ended)
            {
                _entries.Remove(key);
            }
        }

        foreach (var summary in summaries)
        {
            await PublishSummaryAsync(summary).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Publishes the summary of every key with pending repetitions now and forgets all keys, whatever their
    /// window. Hosts call it when they stop, while the inner sink can still write (for example an event store
    /// that opens a service scope per event, which no longer works once the container is being disposed);
    /// <see cref="DisposeAsync"/> calls it as well.
    /// </summary>
    public async Task FlushPendingAsync()
    {
        List<AuditEvent> summaries;
        lock (_lock)
        {
            summaries = _entries.Values.Where(e => e.Repetitions > 0).Select(e => e.BuildSummary(_window)).ToList();
            _entries.Clear();
        }

        foreach (var summary in summaries)
        {
            await PublishSummaryAsync(summary).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the timer and publishes every pending summary.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_timer is not null)
        {
            await _timer.DisposeAsync().ConfigureAwait(false);
        }

        // A flush the timer started has already taken its summaries out of the store; wait until they are published.
        await Volatile.Read(ref _timerFlush).ConfigureAwait(false);
        await FlushPendingAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private AuditEvent? EvictLeastRecentlySeen()
    {
        var oldest = default(KeyValuePair<AggregationKey, Entry>);
        var found = false;
        foreach (var pair in _entries)
        {
            if (!found || pair.Value.LastSeen < oldest.Value.LastSeen)
            {
                oldest = pair;
                found = true;
            }
        }

        if (!found)
        {
            return null;
        }

        _entries.Remove(oldest.Key);
        return oldest.Value.Repetitions > 0 ? oldest.Value.BuildSummary(_window) : null;
    }

    private void OnTimer()
    {
        if (Interlocked.Exchange(ref _flushRunning, 1) == 1)
        {
            return;
        }

        // Remembered so DisposeAsync can wait for a flush the timer started (its summaries are no longer in the store).
        Volatile.Write(ref _timerFlush, FlushDueSafeAsync());
    }

    private async Task FlushDueSafeAsync()
    {

        try
        {
            await FlushDueAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Publishing aggregated audit event summaries failed");
        }
        finally
        {
            Volatile.Write(ref _flushRunning, 0);
        }
    }

    private async Task PublishSummaryAsync(AuditEvent summary)
    {
        try
        {
            await _inner.PublishAsync(summary).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sinks must not throw by contract; a misbehaving one must not stop the other summaries.
            _logger.LogError(ex, "Publishing the aggregated audit event summary '{AuditCategory}' failed",
                summary.Category);
        }
    }

    private readonly record struct AggregationKey(string TenantId, string Key);

    private sealed class Entry(AuditEvent first, DateTimeOffset now)
    {
        public DateTimeOffset WindowStart { get; private set; } = now;
        public DateTimeOffset LastSeen { get; private set; } = now;
        public long Repetitions { get; private set; }
        private DateTimeOffset _firstRepetition;

        public void Count(DateTimeOffset now)
        {
            if (Repetitions == 0)
            {
                _firstRepetition = now;
            }

            Repetitions++;
            LastSeen = now;
        }

        public void StartNewWindow(DateTimeOffset now)
        {
            WindowStart = now;
            Repetitions = 0;
        }

        public AuditEvent BuildSummary(TimeSpan window)
        {
            var firstSeen = _firstRepetition.ToString("O", CultureInfo.InvariantCulture);
            var lastSeen = LastSeen.ToString("O", CultureInfo.InvariantCulture);
            var metadata = first.Metadata is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(first.Metadata);
            metadata["AggregatedCount"] = Repetitions;
            metadata["AggregatedFirstSeen"] = _firstRepetition;
            metadata["AggregatedLastSeen"] = LastSeen;
            metadata["AggregationWindow"] = window;
            return first with
            {
                Message = string.Create(CultureInfo.InvariantCulture,
                    $"{first.Message} [aggregated: {Repetitions} more time(s) from {firstSeen} to {lastSeen}, window {window:c}]"),
                Metadata = metadata,
                // A summary is final; it is never aggregated again.
                AggregationKey = null,
            };
        }
    }
}
