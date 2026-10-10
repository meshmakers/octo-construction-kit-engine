using System.Collections;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts.Exchange;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Exchange;

/// <summary>
/// One attribute of a seed entity that would blank a non-empty value of the existing entity
/// (AB#6315). Carries the raw values so the caller can summarise them; the values themselves must
/// never be logged or returned (credential content).
/// </summary>
/// <param name="Attribute">The CK attribute.</param>
/// <param name="Reason">Why the seed counts as blanking.</param>
/// <param name="ExistingValue">The tenant's current value (repository shape).</param>
/// <param name="SeedPresent">True when the seed declares the attribute.</param>
/// <param name="SeedValue">The seed's value (transport shape), when declared.</param>
internal sealed record SeedBlankingFinding(
    CkTypeAttributeGraph Attribute,
    RtImportBlankingReason Reason,
    object? ExistingValue,
    bool SeedPresent,
    object? SeedValue);

/// <summary>
/// The detection loop shared by the Upsert import (<see cref="ImportRtModelCommand"/>) and the
/// blueprint update preview (AB#6315): for one seed entity over its existing counterpart, which
/// seed-owned attributes does <see cref="SeedValueGuard"/> judge as blanking. One code path, so
/// what the preview announces is exactly what the import guards. Independent of the policy: the
/// policy only decides afterwards whether the finding is kept or applied.
/// </summary>
internal static class SeedBlankingDetector
{
    internal static IReadOnlyList<SeedBlankingFinding> Detect(
        RtEntityTcDto seed,
        RtEntity existing,
        IReadOnlyList<CkTypeAttributeGraph> guardedAttributes)
    {
        var findings = new List<SeedBlankingFinding>();
        foreach (var attr in guardedAttributes)
        {
            var existingPresent = existing.Attributes.TryGetValue(attr.AttributeName, out var existingValue);
            var seedAttr = seed.Attributes.FirstOrDefault(a => a.Id.Equals(attr.CkAttributeId));

            var verdict = SeedValueGuard.Decide(attr.ValueType, attr.Ownership, existingPresent, existingValue,
                seedAttr != null, seedAttr?.Value, RtImportBlankingPolicy.Keep);
            if (verdict.IsBlanking)
            {
                findings.Add(new SeedBlankingFinding(attr, verdict.Blanking, existingValue, seedAttr != null,
                    seedAttr?.Value));
            }
        }

        return findings;
    }

    /// <summary>
    /// A value-free description of a value: its kind and size, never its content. Safe for
    /// credentials, logs and API responses.
    /// </summary>
    internal static string Summarize(object? value, bool present = true)
    {
        if (!present)
        {
            return "omitted";
        }

        return value switch
        {
            null => "null",
            string s when s.Length == 0 => "empty string",
            string s => $"string ({s.Length} chars)",
            byte[] b => $"binary ({b.Length} bytes)",
            RtRecordTcDto => "record",
            RtRecord => "record",
            IEnumerable e => $"array ({e.Cast<object?>().Count()} items)",
            _ => value.GetType().Name.ToLowerInvariant()
        };
    }
}
