using System.Diagnostics.Metrics;

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
}
