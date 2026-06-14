using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.Blueprints;

/// <summary>
///     Replaces <c>${name}</c> placeholders in seed-data entity values using the variable
///     context produced by <see cref="Contracts.Blueprints.IBlueprintVariableProvider"/>.
///     Interpolation runs after deserialisation and before
///     <see cref="BlueprintService"/> stamps blueprint-provenance attributes, so the values
///     written to MongoDB are already resolved.
/// </summary>
/// <remarks>
///     The same <c>${name}</c> syntax is used by the CK-model compiler
///     (<c>ConstructionKit.Engine.Resolvers.VariableResolver</c>) and the mesh-adapter
///     pipeline (<c>PlaceholderReplaceNode</c>). Reusing it here avoids introducing a third
///     interpolation dialect for blueprint authors to learn.
///
///     Substitution targets:
///     <list type="bullet">
///         <item><see cref="RtEntityTcDto.RtWellKnownName"/> — so well-known names can be
///             environment- or tenant-scoped (rarely useful, but consistent).</item>
///         <item>String-valued <see cref="RtAttributeTcDto.Value"/> entries — non-string
///             scalars (numbers, bools, enums, embedded records) are left untouched.</item>
///         <item>String items inside list-valued <see cref="RtAttributeTcDto.Value"/>
///             entries — needed for attributes like <c>RedirectUris</c>,
///             <c>AllowedCorsOrigins</c>, <c>AllowedScopes</c> that carry one placeholder
///             per item. Non-string items inside the list are left untouched.</item>
///     </list>
///     Unknown variables emit an
///     <see cref="MessageLevel.Warning"/> on the <see cref="OperationResult"/> and the
///     placeholder is left verbatim — that way a typo doesn't silently produce empty strings
///     in MongoDB.
/// </remarks>
internal static class BlueprintVariableInterpolator
{
    private static readonly Regex VariablePattern =
        new(@"\${\s*([\w\d._]+)\s*}", RegexOptions.Compiled);

    /// <summary>
    ///     Walks <paramref name="root"/> in place, replacing every <c>${name}</c> with the
    ///     matching entry from <paramref name="variables"/>.
    /// </summary>
    public static void Interpolate(
        RtModelRootTcDto root,
        IReadOnlyDictionary<string, string> variables,
        string sourceDescription,
        OperationResult operationResult)
    {
        if (variables.Count == 0)
        {
            // Nothing to substitute — but blueprint authors may still have written
            // placeholders, so we don't short-circuit Regex.Replace; we just rely on it
            // to be a fast no-op when the input contains no ${...} sequences.
        }

        foreach (var entity in root.Entities)
        {
            if (!string.IsNullOrEmpty(entity.RtWellKnownName))
            {
                entity.RtWellKnownName = InterpolateString(
                    entity.RtWellKnownName!, variables, sourceDescription, operationResult);
            }

            foreach (var attribute in entity.Attributes)
            {
                switch (attribute.Value)
                {
                    case string scalar:
                        attribute.Value = InterpolateString(
                            scalar, variables, sourceDescription, operationResult);
                        break;
                    case IList<object> list:
                        // YAML deserialisation of a list-valued attribute (e.g. RedirectUris,
                        // AllowedCorsOrigins, AllowedScopes) materialises as IList<object>.
                        // Walk the items in place so string entries get interpolated while
                        // non-string entries (numeric enum codes, embedded records) survive
                        // untouched.
                        for (var i = 0; i < list.Count; i++)
                        {
                            if (list[i] is string item)
                            {
                                list[i] = InterpolateString(
                                    item, variables, sourceDescription, operationResult);
                            }
                        }
                        break;
                }
            }
        }
    }

    private static string InterpolateString(
        string value,
        IReadOnlyDictionary<string, string> variables,
        string sourceDescription,
        OperationResult operationResult)
    {
        if (value.IndexOf("${", StringComparison.Ordinal) < 0)
        {
            return value;
        }

        return VariablePattern.Replace(value, match =>
        {
            var name = match.Groups[1].Value;
            if (variables.TryGetValue(name, out var replacement))
            {
                return replacement;
            }

            operationResult.AddMessage(new OperationMessage(
                MessageLevel.Warning,
                sourceDescription,
                27,
                $"Unknown blueprint variable '${{{name}}}' — placeholder left unchanged."));
            return match.Value;
        });
    }
}
