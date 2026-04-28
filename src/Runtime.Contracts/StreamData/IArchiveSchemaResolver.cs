using System.Collections.Generic;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolves attribute paths declared on <c>CkArchiveColumn</c> against a CK type's attribute graph,
/// producing <see cref="ArchiveColumnSpec"/> entries that downstream DDL generation and validation
/// consume. DB-neutral.
/// </summary>
public interface IArchiveSchemaResolver
{
    /// <summary>
    /// Resolves a single attribute path against <paramref name="targetCkTypeId"/>. Returns a
    /// <see cref="ArchiveColumnSpec"/> describing the column shape. Throws
    /// <c>ArchivePathInvalidException</c> when the path cannot be resolved (unknown attribute,
    /// broken record traversal, illegal array indexing).
    /// </summary>
    ArchiveColumnSpec ResolvePath(RtCkId<CkTypeId> targetCkTypeId, string path);

    /// <summary>
    /// Enumerates all attribute paths reachable from <paramref name="targetCkTypeId"/> recursively
    /// via record traversal up to <paramref name="maxDepth"/>. Used by the studio's path picker
    /// (GraphQL <c>availableArchivePaths</c> resolver).
    /// </summary>
    IEnumerable<ArchiveColumnSpec> EnumerateAvailablePaths(
        RtCkId<CkTypeId> targetCkTypeId, int maxDepth = 5);
}
