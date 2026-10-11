using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     AB#6295: validates the <c>compatibility.acknowledge</c> section of <c>ckModel.yaml</c> (message 130) and
///     normalizes it for the compiled model. The schema already rejects an empty reason and wildcard keys; this is the
///     same rule for sources that did not go through the schema, plus duplicate keys.
/// </summary>
internal static class CkCompatibilityValidator
{
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
        foreach (var (index, reason) in CkAcknowledgeEntryRules.FindProblems(entries))
        {
            Add(operationResult, location, meta, index, reason);
        }

        var normalized = entries
            .Select(e => new CkAcknowledgeDto { Change = (e.Change ?? "").Trim(), Reason = (e.Reason ?? "").Trim() })
            .ToList();

        return new CkCompatibilityDto { Acknowledge = normalized };
    }

    private static void Add(OperationResult operationResult, string? location, CkMetaRootDto meta, int index,
        string reason) =>
        operationResult.AddMessage(MessageCodes.InvalidCompatibilityAcknowledge(location, meta.ModelId, index, reason));
}
