using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Repositories.Query;

/// <summary>
///     Implements the statistic functions for a result set.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class RtStatisticFunctions<TEntity> where TEntity : RtEntity
{
    private readonly CkTypeGraph _ckTypeGraph;
    private readonly FieldGroupBy _groupBy;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="ckTypeGraph">The type graph</param>
    /// <param name="groupBy">The grouping information</param>
    public RtStatisticFunctions(CkTypeGraph ckTypeGraph, FieldGroupBy groupBy)
    {
        _ckTypeGraph = ckTypeGraph;
        _groupBy = groupBy;
    }

    /// <summary>
    ///     Calculates the grouping result
    /// </summary>
    /// <param name="resultList">Result list the grouping needs to be executed.</param>
    /// <returns></returns>
    public IEnumerable<GroupingResult> Calculate(IEnumerable<TEntity> resultList)
    {
        var groupByPropertiesResult =
            resultList.GroupBy(g => new Key(_groupBy.GroupByAttributeNameList.Select(a => GetValue(a)(g))));

        var calculateGrouping = new List<GroupingResult>();
        foreach (var entityGrouping in groupByPropertiesResult.OrderBy(x => x.Key))
        {
            var grouping = new GroupingResult(
                _groupBy.GroupByAttributeNameList,
                entityGrouping.Key.Keys,
                entityGrouping.Count(),
                RunStatistics(_groupBy.CountAttributePathList,
                    attributeName => entityGrouping.Count(x => GetValue(attributeName)(x) != null)),
                RunStatistics(_groupBy.MinValueAttributePathList,
                    attributeName => entityGrouping.Min(x => GetValue(attributeName)(x))),
                RunStatistics(_groupBy.MaxValueAttributePathList,
                    attributeName => entityGrouping.Max(x => GetValue(attributeName)(x))),
                RunStatistics(_groupBy.AvgAttributePathList,
                    attributeName => entityGrouping.Average(x => (decimal?)GetValue(attributeName)(x)))
            );
            calculateGrouping.Add(grouping);
        }

        return calculateGrouping;
    }

    private static List<StatisticsResult> RunStatistics(IEnumerable<string>? attributePaths,
        Func<string, object?> calcFunction)
    {
        var list = new List<StatisticsResult>();
        if (attributePaths != null)
        {
            foreach (var attributeName in attributePaths)
            {
                list.Add(new StatisticsResult
                {
                    AttributeName = attributeName,
                    Value = calcFunction(attributeName)
                });
            }
        }

        return list;
    }

    private Func<TEntity, object?> GetValue(string propertyName)
    {
        return entity =>
        {
            if (_ckTypeGraph.AllAttributesByName.TryGetValue(propertyName, out var attributeCacheItem))
            {
                if (entity.Attributes.TryGetValue(propertyName, out var value))
                {
                    return AttributeValueConverter.ConvertAttributeValue(attributeCacheItem.ValueType, value);
                }

                return null;
            }

            throw RuntimeRepositoryException.AttributeWithNameDoesNotExist(_ckTypeGraph.CkTypeId, propertyName);
        };
    }

    private class Key : IComparable
    {
        public Key(IEnumerable<object?> keys)
        {
            Keys = keys;
        }

        public IEnumerable<object?> Keys { get; }

        public int CompareTo(object? obj)
        {
            if (obj == null)
            {
                return 1;
            }

            if (!(obj is Key key1))
            {
                return 1;
            }

            var keys = Keys.ToArray();
            var otherKeys = key1.Keys.ToArray();
            if (keys.Length < otherKeys.Length)
            {
                return -1;
            }

            if (keys.Length > otherKeys.Length)
            {
                return 1;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var otherKey = otherKeys[i];

                if (key == null && otherKey != null)
                {
                    return -1;
                }

                if (key is IComparable comparable)
                {
                    var result = comparable.CompareTo(otherKey);
                    if (result != 0)
                    {
                        return result;
                    }
                }
                else
                {
                    throw RuntimeRepositoryException.NotComparable(key);
                }
            }

            return 0;
        }

        public override bool Equals(object? obj)
        {
            var t = obj is Key other && Keys.SequenceEqual(other.Keys);
            return t;
        }

        public override int GetHashCode()
        {
            return Keys.Aggregate(0, (current, key) => current ^ key?.GetHashCode() ?? 0);
        }
    }
}