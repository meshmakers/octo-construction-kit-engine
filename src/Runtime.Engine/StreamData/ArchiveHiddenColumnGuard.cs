using System;
using System.Collections.Generic;
using System.Linq;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.StreamData;

namespace Meshmakers.Octo.Runtime.Engine.StreamData;

/// <summary>
///     CK v2 (F1.2-S2, review M12 / G3 E-M2): stream-data storage and queries (CrateDB) do not enforce attribute
///     access, so no archive column may reach a Hidden attribute. A path reaches Hidden when one of its segments is
///     Hidden, or when it ends at a record (or record array) whose record — transitively — contains a Hidden
///     sub-attribute (a whole-record column would store it). Segments match case-insensitively; <c>[*]</c>
///     projections are ignored. Used on activation, retry, re-provisioning and re-validation
///     (<see cref="ArchiveLifecycleService" />), and available to the ingest and query paths.
/// </summary>
public static class ArchiveHiddenColumnGuard
{
    /// <summary>
    ///     The Hidden attribute a column path reaches (<c>Record.Sub</c> notation for a record sub-attribute), or
    ///     <c>null</c>. Unknown segments end the walk (reported elsewhere).
    /// </summary>
    public static string? FindHiddenAttribute(ICkCacheService cache, string tenantId, RtCkId<CkTypeId> targetCkTypeId,
        string path)
    {
        CkTypeWithAttributesGraph? current;
        try
        {
            current = cache.GetRtCkType(tenantId, targetCkTypeId);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }

        var segments = path.Split('.').Select(s => s.Replace("[*]", "")).ToList();
        for (var i = 0; i < segments.Count && current != null; i++)
        {
            var attribute = Find(current, segments[i]);
            if (attribute == null)
            {
                return null;
            }

            if (attribute.Access == CkAttributeAccessDto.Hidden)
            {
                return attribute.AttributeName;
            }

            var record = RecordOf(cache, tenantId, attribute);
            if (i == segments.Count - 1)
            {
                // A whole-record column stores every sub-attribute.
                return record == null
                    ? null
                    : FindHiddenInRecord(cache, tenantId, record, attribute.AttributeName, []);
            }

            current = record;
        }

        return null;
    }

    /// <summary>
    ///     The first ingested column of the archive that reaches a Hidden attribute. Rollup columns are derived from
    ///     source archives (checked themselves); computed columns reference other columns of the same row.
    /// </summary>
    public static (string Path, string Attribute)? FindHiddenColumn(ICkCacheService cache, string tenantId,
        ArchiveSnapshot snapshot)
    {
        if (snapshot.RollupAggregations is not null)
        {
            return null;
        }

        foreach (var column in snapshot.Columns.Where(c => !c.IsComputed && !string.IsNullOrWhiteSpace(c.Path)))
        {
            var hidden = FindHiddenAttribute(cache, tenantId, snapshot.TargetCkTypeId, column.Path);
            if (hidden != null)
            {
                return (column.Path, hidden);
            }
        }

        return null;
    }

    private static CkTypeAttributeGraph? Find(CkTypeWithAttributesGraph scope, string segment) =>
        scope.AllAttributesByName.TryGetValue(segment, out var exact)
            ? exact
            : scope.AllAttributesByName
                .FirstOrDefault(a => string.Equals(a.Key, segment, StringComparison.OrdinalIgnoreCase)).Value;

    private static CkRecordGraph? RecordOf(ICkCacheService cache, string tenantId, CkTypeAttributeGraph attribute)
    {
        if (attribute.ValueCkRecordId == null)
        {
            return null;
        }

        try
        {
            return cache.GetRtCkRecord(tenantId, attribute.ValueCkRecordId.ToRtCkId());
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static string? FindHiddenInRecord(ICkCacheService cache, string tenantId, CkRecordGraph record,
        string prefix, HashSet<CkId<CkRecordId>> visited)
    {
        if (!visited.Add(record.CkRecordId))
        {
            return null;
        }

        foreach (var sub in record.AllAttributes.Values)
        {
            var name = $"{prefix}.{sub.AttributeName}";
            if (sub.Access == CkAttributeAccessDto.Hidden)
            {
                return name;
            }

            var nested = RecordOf(cache, tenantId, sub);
            var found = nested == null ? null : FindHiddenInRecord(cache, tenantId, nested, name, visited);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
