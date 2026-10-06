using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Meshmakers.Octo.Runtime.Contracts.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Meter for Secret attribute access (AB#5528, concept §3.7, §5.3). Hosts must register
///     <see cref="MeterName" /> with their meter provider (octo-common-services
///     <c>ObservabilityBuilder</c>) - an unregistered meter drops every measurement silently.
/// </summary>
public static class SecretDiagnostics
{
    /// <summary>
    ///     Meter name.
    /// </summary>
    public const string MeterName = "Meshmakers.Octo.Secrets";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    /// <summary>
    ///     Incremented on every decrypt of a secret value. Tags: <c>tenant</c>, <c>ckType</c>,
    ///     <c>attribute</c>, <c>service</c> (where known) and <c>form</c> (<c>enc_v2</c>, <c>enc_v1</c>,
    ///     <c>plaintext</c>, <c>pending</c>). Never carries a value.
    /// </summary>
    public static readonly Counter<long> Decrypts = Meter.CreateCounter<long>(
        "octo.secrets.decrypt",
        unit: "{decrypt}",
        description: "Count of server-side decrypts of Secret attribute values.");

    /// <summary>
    ///     Incremented when a Secret slot still held clear text (legacy plaintext read). Should drop to
    ///     zero after the encrypt sweep; strict mode makes such reads fail (concept §5.2 phase 5).
    /// </summary>
    public static readonly Counter<long> PlaintextReads = Meter.CreateCounter<long>(
        "octo.secrets.plaintext_reads",
        unit: "{read}",
        description: "Count of reads of Secret attribute values that were still stored as clear text.");

    /// <summary>
    ///     Incremented when strict mode (<c>SecretEncryption:StrictMode</c>, concept §5.2 phase 5) rejected
    ///     a read of a Secret value still stored as clear text. Tags: <c>tenant</c>, <c>ckType</c>,
    ///     <c>attribute</c>, <c>service</c> (where known). Never carries a value.
    /// </summary>
    public static readonly Counter<long> StrictModeRejectedReads = Meter.CreateCounter<long>(
        "octo.secrets.strict_mode.rejected_reads",
        unit: "{read}",
        description: "Count of legacy plaintext Secret reads rejected by strict mode.");

    /// <summary>
    ///     Incremented by the secret sweep for every value it changed (AB#5532). Tags: <c>tenant</c>,
    ///     <c>mode</c> (<c>verify</c>, <c>encrypt</c>, <c>reprotect</c>, <c>clear_unknown_kid</c>,
    ///     <c>decrypt</c>, <c>normalize_placeholders</c>).
    /// </summary>
    public static readonly Counter<long> SweepValuesRewritten = Meter.CreateCounter<long>(
        "octo.secrets.sweep.rewritten",
        unit: "{value}",
        description: "Count of Secret attribute values changed by the secret sweep.");

    /// <summary>
    ///     Incremented by the secret sweep for every value it could not process (AB#5532). Tags:
    ///     <c>tenant</c>, <c>mode</c>.
    /// </summary>
    public static readonly Counter<long> SweepFailures = Meter.CreateCounter<long>(
        "octo.secrets.sweep.failed",
        unit: "{value}",
        description: "Count of Secret attribute values the secret sweep could not process.");

    private static readonly ConcurrentDictionary<string, IReadOnlyList<Measurement<long>>> LastSweepValues =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     <c>octo.secrets.values{tenant, model, form, kid}</c> (concept §5.3): the stored forms found by the
    ///     LAST sweep of each tenant in this process. Only the process that runs the sweeps (the bot
    ///     <c>SecretSweepJob</c>, WP9) reports it; the alert "plaintext &gt; 0 after strict mode" builds on it.
    ///     The <c>env</c> dimension comes from the OTel resource attributes of the host.
    /// </summary>
    // ReSharper disable once UnusedMember.Local - the instrument lives as long as the meter.
    private static readonly ObservableGauge<long> Values = Meter.CreateObservableGauge(
        "octo.secrets.values",
        () => LastSweepValues.Values.SelectMany(v => v),
        unit: "{value}",
        description: "Secret attribute values per stored form, as found by the last secret sweep of a tenant.");

    /// <summary>
    ///     Records the forms found by a sweep as the current value of <c>octo.secrets.values</c> for the
    ///     tenant (replacing the previous sweep's values).
    /// </summary>
    /// <param name="tenantId">Tenant</param>
    /// <param name="countsPerModel">Counts per CK model name</param>
    public static void RecordSweepValues(string tenantId, IReadOnlyDictionary<string, SecretFormCounts> countsPerModel)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(countsPerModel);

        var measurements = new List<Measurement<long>>();
        foreach (var (model, counts) in countsPerModel)
        {
            Add(measurements, tenantId, model, "not_set", null, counts.NotSet);
            Add(measurements, tenantId, model, "placeholder", null, counts.Placeholder);
            Add(measurements, tenantId, model, "plaintext", null, counts.Plaintext);
            Add(measurements, tenantId, model, "enc_v1", null, counts.EncV1);
            foreach (var (keyId, count) in counts.EncV2ByKeyId)
            {
                Add(measurements, tenantId, model, "enc_v2", keyId, count);
            }

            foreach (var (keyId, count) in counts.UnknownKeyIdByKeyId)
            {
                Add(measurements, tenantId, model, "unknown_kid", keyId, count);
            }
        }

        LastSweepValues[tenantId] = measurements;
    }

    private static void Add(List<Measurement<long>> measurements, string tenantId, string model, string form,
        string? keyId, long count)
    {
        var tags = new TagList { { "tenant", tenantId }, { "model", model }, { "form", form } };
        if (keyId != null)
        {
            tags.Add("kid", keyId);
        }

        measurements.Add(new Measurement<long>(count, tags));
    }
}
