using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
///     Evaluates a blueprint's <c>requires:</c> block against a tenant's resolved variable
///     context. Exposed as an internal helper so <see cref="BlueprintService"/> and the unit
///     tests share the same code path — the semantics (missing variable fails, empty
///     allow-list always fails, ordinal string compare) live in one place.
/// </summary>
internal static class RequiresEvaluator
{
    /// <summary>
    ///     Returns <c>null</c> when every entry in <paramref name="requires"/> is satisfied
    ///     by <paramref name="variables"/>, otherwise a human-readable reason describing
    ///     the first failing requirement.
    /// </summary>
    public static string? FindMismatch(
        RequiresMap requires,
        IReadOnlyDictionary<string, string> variables)
    {
        foreach (var entry in requires)
        {
            var key = entry.Key;
            var allowedValues = entry.Value;

            if (!variables.TryGetValue(key, out var actual))
            {
                return $"required variable '{key}' is not defined in the tenant's variable context";
            }

            if (allowedValues.Count == 0)
            {
                return $"required variable '{key}' has an empty allow-list — nothing can match";
            }

            if (!allowedValues.Contains(actual, StringComparer.Ordinal))
            {
                var allowed = string.Join(", ", allowedValues);
                return $"required variable '{key}' is '{actual}', expected one of [{allowed}]";
            }
        }

        return null;
    }
}
