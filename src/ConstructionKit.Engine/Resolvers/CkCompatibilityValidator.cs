using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     AB#6295: validates the <c>compatibility.acknowledge</c> section of <c>ckModel.yaml</c> (message 130) and
///     normalizes it for the compiled model. The schema already rejects an empty reason and wildcard keys; this is the
///     same rule for sources that did not go through the schema, plus duplicate keys.
/// </summary>
internal static class CkCompatibilityValidator
{
    private static readonly char[] Wildcards = ['*', '?'];

    /// <summary>
    ///     Returns the normalized section (trimmed, in declared order), or null when the model acknowledges nothing.
    /// </summary>
    internal static CkCompatibilityDto? Validate(CkMetaRootDto meta, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var entries = meta.Compatibility?.Acknowledge;
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        var location = originFileResolver.Resolve(meta.ModelId);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<CkAcknowledgeDto>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var change = (entry.Change ?? "").Trim();
            var reason = (entry.Reason ?? "").Trim();
            var index = i + 1;
            if (change.Length == 0)
            {
                Add(operationResult, location, meta, index, "'change' must not be empty");
            }
            else if (change.IndexOfAny(Wildcards) >= 0)
            {
                Add(operationResult, location, meta, index,
                    $"'change' must be the exact change key printed by the compile gate; wildcards are not allowed ('{change}')");
            }
            else if (!seen.Add(change))
            {
                Add(operationResult, location, meta, index, $"the change '{change}' is acknowledged more than once");
            }

            if (reason.Length == 0)
            {
                Add(operationResult, location, meta, index, "'reason' is mandatory and must not be empty");
            }

            normalized.Add(new CkAcknowledgeDto { Change = change, Reason = reason });
        }

        return new CkCompatibilityDto { Acknowledge = normalized };
    }

    private static void Add(OperationResult operationResult, string? location, CkMetaRootDto meta, int index,
        string reason) =>
        operationResult.AddMessage(MessageCodes.InvalidCompatibilityAcknowledge(location, meta.ModelId, index, reason));
}
