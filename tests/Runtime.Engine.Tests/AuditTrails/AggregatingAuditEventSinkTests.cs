using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Meshmakers.Octo.Runtime.Contracts.AuditTrails;
using Meshmakers.Octo.Runtime.Contracts.DataPermissions;
using Meshmakers.Octo.Runtime.Engine.AuditTrails;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.Runtime.Engine.Tests.AuditTrails;

/// <summary>
///     AB#7279: repeated AuditOnly data-permission violations are published once per key and window, the counter
///     counts every occurrence, the key store is bounded and pending summaries survive shutdown.
/// </summary>
public class AggregatingAuditEventSinkTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task SameKey_InOneWindow_PublishesFirstAtOnceAndOneSummaryWithTheRepetitions()
    {
        var (sink, inner, clock) = Create();
        const int n = 250;

        for (var i = 0; i < n; i++)
        {
            await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1",
                DataPermissionAudit.Actions.Read);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        var first = Assert.Single(inner.Events);
        Assert.Equal(DataPermissionAudit.ReadViolationCategory, first.Category);
        Assert.Equal("Subject 'sa-1' read protected type 'Acc/Doc-1' without a grant (AuditOnly policy — not blocked).",
            first.Message);

        clock.Advance(Window);
        await sink.FlushDueAsync();

        Assert.Equal(2, inner.Events.Count);
        var summary = inner.Events[1];
        Assert.Equal(DataPermissionAudit.ReadViolationCategory, summary.Category);
        Assert.Equal("t1", summary.TenantId);
        Assert.Null(summary.AggregationKey);
        Assert.Equal((long)(n - 1), summary.Metadata!["AggregatedCount"]);
        Assert.Contains($"[aggregated: {n - 1} more time(s) from ", summary.Message, StringComparison.Ordinal);
        Assert.StartsWith(first.Message, summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counter_CountsEveryOccurrence_IncludingTheAggregatedOnes()
    {
        var tenant = "counter-" + Guid.NewGuid().ToString("N");
        using var counter = new ViolationCounter(tenant);
        var (sink, inner, _) = Create();
        const int n = 40;

        for (var i = 0; i < n; i++)
        {
            await DataPermissionAudit.PublishReadViolationAsync(sink, tenant, "sa-1", "Acc/Doc-1",
                DataPermissionAudit.Actions.Read);
        }

        Assert.Single(inner.Events);
        Assert.Equal(n, counter.Total);
        Assert.All(counter.Tags, t =>
        {
            Assert.Equal(tenant, t["tenant"]);
            Assert.Equal("sa-1", t["subject"]);
            Assert.Equal("Acc/Doc-1", t["type"]);
            Assert.Equal("read", t["action"]);
        });
    }

    [Fact]
    public async Task DifferentActions_AreDifferentKeys_AndNameTheAction()
    {
        var (sink, inner, _) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", DataPermissionAudit.Actions.Read);
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", DataPermissionAudit.Actions.Update);
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", DataPermissionAudit.Actions.Replace);
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", DataPermissionAudit.Actions.Delete);
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", DataPermissionAudit.Actions.Update);

        Assert.Equal(4, inner.Events.Count);
        Assert.Contains("read protected type", inner.Events[0].Message, StringComparison.Ordinal);
        Assert.Contains("performed 'update' on protected type", inner.Events[1].Message, StringComparison.Ordinal);
        Assert.Contains("performed 'replace' on protected type", inner.Events[2].Message, StringComparison.Ordinal);
        Assert.Contains("performed 'delete' on protected type", inner.Events[3].Message, StringComparison.Ordinal);
        Assert.Equal(["read", "update", "replace", "delete"],
            inner.Events.Select(e => (string)e.Metadata!["Action"]!).ToArray());
    }

    [Fact]
    public async Task DifferentTenantsSubjectsAndTypes_AreDifferentKeys()
    {
        var (sink, inner, _) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t2", "sa-1", "Acc/Doc-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-2", "Acc/Doc-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Bank-1", "read");
        await DataPermissionAudit.PublishWriteViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", RtDataAction.Write);
        await DataPermissionAudit.PublishWriteViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", RtDataAction.Write,
            entityNotVisible: true);

        Assert.Equal(6, inner.Events.Count);
    }

    [Fact]
    public async Task KeyWithoutRepetitions_PublishesNoSummary_AndIsReportedAtOnceInTheNextWindow()
    {
        var (sink, inner, clock) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
        clock.Advance(Window);
        await sink.FlushDueAsync();

        Assert.Single(inner.Events);
        Assert.Equal(0, sink.KeyCount);

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
        Assert.Equal(2, inner.Events.Count);
        Assert.NotNull(inner.Events[1].AggregationKey);
    }

    [Fact]
    public async Task ContinuousRepetitions_PublishOneSummaryPerWindow()
    {
        var (sink, inner, clock) = Create();

        for (var window = 0; window < 3; window++)
        {
            for (var i = 0; i < 10; i++)
            {
                await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
                clock.Advance(TimeSpan.FromSeconds(10));
            }

            clock.Advance(Window);
            await sink.FlushDueAsync();
        }

        // First occurrence at once, then one summary per window: 9 + 10 + 10 repetitions.
        Assert.Equal(4, inner.Events.Count);
        Assert.Equal([9L, 10L, 10L],
            inner.Events.Skip(1).Select(e => (long)e.Metadata!["AggregatedCount"]!).ToArray());
    }

    [Fact]
    public async Task WindowNotEnded_PublishesNoSummaryYet()
    {
        var (sink, inner, clock) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "Acc/Doc-1", "read");
        clock.Advance(Window - TimeSpan.FromSeconds(1));
        await sink.FlushDueAsync();

        Assert.Single(inner.Events);
        Assert.Equal(1, sink.KeyCount);
    }

    [Fact]
    public async Task BoundedStore_EvictsLeastRecentlySeenKey_AndPublishesItsSummary()
    {
        var (sink, inner, clock) = Create(maxKeys: 2);

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        clock.Advance(TimeSpan.FromSeconds(1));
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "B/B-1", "read");
        clock.Advance(TimeSpan.FromSeconds(1));
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "C/C-1", "read");

        Assert.Equal(2, sink.KeyCount);
        // A first, B first, A's summary (evicted), C first.
        Assert.Equal(4, inner.Events.Count);
        var evicted = inner.Events[2];
        Assert.Contains("'A/A-1'", evicted.Message, StringComparison.Ordinal);
        Assert.Equal(2L, evicted.Metadata!["AggregatedCount"]);
        Assert.Contains("'C/C-1'", inner.Events[3].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_FlushesPendingSummaries_AndLaterEventsPassThrough()
    {
        var (sink, inner, _) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "B/B-1", "read");

        await sink.DisposeAsync();

        Assert.Equal(3, inner.Events.Count);
        Assert.Equal(1L, inner.Events[2].Metadata!["AggregatedCount"]);

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        Assert.Equal(4, inner.Events.Count);
    }

    [Fact]
    public async Task FlushPending_PublishesPendingSummaries_AndKeepsAggregating()
    {
        var (sink, inner, _) = Create();

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "B/B-1", "read");

        await sink.FlushPendingAsync();

        Assert.Equal(3, inner.Events.Count);
        Assert.Equal(1L, inner.Events[2].Metadata!["AggregatedCount"]);
        Assert.Equal(0, sink.KeyCount);

        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        Assert.Equal(4, inner.Events.Count);
    }

    [Fact]
    public async Task Dispose_WaitsForASummaryTheTimerIsStillPublishing()
    {
        var release = new TaskCompletionSource();
        var inner = new BlockingSummarySink(release.Task);
        var clock = new ManualTimeProvider();
        var sink = new AggregatingAuditEventSink(inner,
            Options.Create(new AuditEventAggregationOptions { Window = Window }),
            NullLogger<AggregatingAuditEventSink>.Instance, clock);
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        clock.Advance(Window);

        clock.FireTimers();
        var dispose = sink.DisposeAsync().AsTask();
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(dispose.IsCompleted);
        release.SetResult();
        await dispose;
        Assert.Equal(1, inner.Summaries);
    }

    [Fact]
    public async Task EventsWithoutKey_PassThroughOneByOne()
    {
        var (sink, inner, _) = Create();
        var auditEvent = new AuditEvent("t1", AuditEventLevel.Warning, "DataPermissions.BlueprintLockViolation", "x");

        await sink.PublishAsync(auditEvent, TestContext.Current.CancellationToken);
        await sink.PublishAsync(auditEvent, TestContext.Current.CancellationToken);

        Assert.Equal(2, inner.Events.Count);
    }

    [Fact]
    public async Task Disabled_PublishesEveryEvent()
    {
        var (sink, inner, _) = Create(enabled: false);

        for (var i = 0; i < 5; i++)
        {
            await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
        }

        Assert.Equal(5, inner.Events.Count);
    }

    [Fact]
    public async Task SummaryFailure_OfTheInnerSink_DoesNotStopTheOtherSummaries()
    {
        var inner = new CapturingSink { FailOnSummary = "A/A-1" };
        var clock = new ManualTimeProvider();
        var sink = new AggregatingAuditEventSink(inner, Options.Create(new AuditEventAggregationOptions()),
            NullLogger<AggregatingAuditEventSink>.Instance, clock);
        for (var i = 0; i < 2; i++)
        {
            await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "A/A-1", "read");
            await DataPermissionAudit.PublishReadViolationAsync(sink, "t1", "sa-1", "B/B-1", "read");
        }

        clock.Advance(Window);
        await sink.FlushDueAsync();

        Assert.Contains(inner.Events, e => e.Message.Contains("'B/B-1'", StringComparison.Ordinal) &&
                                           e.Metadata!.ContainsKey("AggregatedCount"));
    }

    [Fact]
    public async Task AddRuntimeEngine_WrapsTheHostSink_AndBindsTheWindowFromConfiguration()
    {
        var inner = new CapturingSink();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AuditEventAggregation:Window"] = "00:05:00",
                ["AuditEventAggregation:MaxKeys"] = "7",
            }).Build());
        services.AddSingleton<IAuditEventSink>(inner);
        services.AddRuntimeEngine();
        await using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AuditEventAggregationOptions>>().Value;
        Assert.Equal(TimeSpan.FromMinutes(5), options.Window);
        Assert.Equal(7, options.MaxKeys);
        // The host's sink stays the registered IAuditEventSink.
        Assert.Same(inner, provider.GetRequiredService<IAuditEventSink>());

        var aggregator = provider.GetRequiredService<AggregatingAuditEventSink>();
        await DataPermissionAudit.PublishReadViolationAsync(aggregator, "t1", "sa-1", "A/A-1", "read");
        await DataPermissionAudit.PublishReadViolationAsync(aggregator, "t1", "sa-1", "A/A-1", "read");
        Assert.Single(inner.Events);

        await provider.DisposeAsync();
        Assert.Equal(2, inner.Events.Count);
    }

    private static (AggregatingAuditEventSink Sink, CapturingSink Inner, ManualTimeProvider Clock) Create(
        int maxKeys = 1000, bool enabled = true)
    {
        var inner = new CapturingSink();
        var clock = new ManualTimeProvider();
        var sink = new AggregatingAuditEventSink(inner,
            Options.Create(new AuditEventAggregationOptions { Window = Window, MaxKeys = maxKeys, Enabled = enabled }),
            NullLogger<AggregatingAuditEventSink>.Instance, clock);
        return (sink, inner, clock);
    }

    private sealed class CapturingSink : IAuditEventSink
    {
        private readonly List<AuditEvent> _events = [];

        public string? FailOnSummary { get; init; }

        public List<AuditEvent> Events
        {
            get
            {
                lock (_events)
                {
                    return [.. _events];
                }
            }
        }

        public Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (FailOnSummary != null && auditEvent.AggregationKey == null &&
                auditEvent.Message.Contains(FailOnSummary, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("sink down");
            }

            lock (_events)
            {
                _events.Add(auditEvent);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Sink whose summaries (events without key) wait for <c>release</c>.</summary>
    private sealed class BlockingSummarySink(Task release) : IAuditEventSink
    {
        private int _summaries;

        public int Summaries => Volatile.Read(ref _summaries);

        public async Task PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (auditEvent.AggregationKey == null)
            {
                await release;
                Interlocked.Increment(ref _summaries);
            }
        }
    }

    /// <summary>Clock under test control; its timers fire only on <see cref="FireTimers" />.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State)> _timers = [];
        private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public void FireTimers()
        {
            foreach (var (callback, state) in _timers)
            {
                callback(state);
            }
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timers.Add((callback, state));
            return new ManualTimer();
        }

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Listens to the violation counter for one tenant (tests run in parallel on the static meter).</summary>
    private sealed class ViolationCounter : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _total;

        public ViolationCounter(string tenant)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DataPermissionAudit.MeterName &&
                    instrument.Name == DataPermissionAudit.ViolationsMetricName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                var map = new Dictionary<string, object?>();
                foreach (var tag in tags)
                {
                    map[tag.Key] = tag.Value;
                }

                if (Equals(map.GetValueOrDefault("tenant"), tenant))
                {
                    Interlocked.Add(ref _total, value);
                    Tags.Add(map);
                }
            });
            _listener.Start();
        }

        public long Total => Interlocked.Read(ref _total);

        public ConcurrentBag<Dictionary<string, object?>> Tags { get; } = [];

        public void Dispose() => _listener.Dispose();
    }
}
