using Meshmakers.Octo.ConstructionKit.Contracts.Services;
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
    private readonly ICkCacheService _ckCacheService;
    private readonly string _tenantId;
    private readonly AggregationInput? _resultResultAggregation;
    private readonly FieldAggregationInput? _fieldAggregation;

    /// <summary>
    ///     Constructor
    /// </summary>
    /// <param name="ckCacheService">The cache service</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <param name="resultAggregation">The result aggregation input</param>
    /// <param name="fieldAggregation">The field aggregation input</param>
    public RtStatisticFunctions(ICkCacheService ckCacheService, string tenantId, AggregationInput? resultAggregation,
        FieldAggregationInput? fieldAggregation)
    {
        _ckCacheService = ckCacheService;
        _tenantId = tenantId;
        _resultResultAggregation = resultAggregation;
        _fieldAggregation = fieldAggregation;
    }

    /// <summary>
    ///     Calculates the result aggregation
    /// </summary>
    /// <param name="resultList">Result list the grouping needs to be executed.</param>
    /// <returns></returns>
    public AggregationResult? CalculateResultAggregation(IEnumerable<TEntity> resultList)
    {
        if (_resultResultAggregation == null)
        {
            return null;
        }

        var entityGroupings = resultList as TEntity[] ?? resultList.ToArray();
        var aggregationResult = new AggregationResult(
            entityGroupings.Count(),
            RunStatistics(_resultResultAggregation.CountAttributePathList,
                attributePath => entityGroupings.Count(x =>
                    x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath) != null)),
            RunStatistics(_resultResultAggregation.MinValueAttributePathList,
                attributePath => entityGroupings.Min(x =>
                    ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
            RunStatistics(_resultResultAggregation.MaxValueAttributePathList,
                attributePath => entityGroupings.Max(x =>
                    ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
            RunStatistics(_resultResultAggregation.AvgAttributePathList,
                attributePath => entityGroupings.Average(x =>
                    ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
            RunStatistics(_resultResultAggregation.SumAttributePathList,
                attributePath => entityGroupings.Sum(x =>
                    ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath))))
        );

        return aggregationResult;
    }


    /// <summary>
    ///     Calculates the field aggregation using grouping by specific attributes.
    /// </summary>
    /// <param name="resultList">Result list the grouping needs to be executed.</param>
    /// <returns></returns>
    public IEnumerable<FieldAggregationResult> CalculateFieldAggregation(IEnumerable<TEntity> resultList)
    {
        if (_fieldAggregation == null)
        {
            return new List<FieldAggregationResult>();
        }

        var groupByPropertiesResult =
            resultList.GroupBy(g =>
                new Key(_fieldAggregation.GroupByAttributePathList.Select(a =>
                    g.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, a))));

        var calculateGrouping = new List<FieldAggregationResult>();
        foreach (var entityGrouping in groupByPropertiesResult.OrderBy(x => x.Key))
        {
            var grouping = new FieldAggregationResult(
                _fieldAggregation.GroupByAttributePathList,
                entityGrouping.Key.Keys,
                entityGrouping.Count(),
                RunStatistics(_fieldAggregation.CountAttributePathList,
                    attributePath => entityGrouping.Count(x =>
                        x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath) != null)),
                RunStatistics(_fieldAggregation.MinValueAttributePathList,
                    attributePath => entityGrouping.Min(x =>
                        ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
                RunStatistics(_fieldAggregation.MaxValueAttributePathList,
                    attributePath => entityGrouping.Max(x =>
                        ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
                RunStatistics(_fieldAggregation.AvgAttributePathList,
                    attributePath => entityGrouping.Average(x =>
                        ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath)))),
                RunStatistics(_fieldAggregation.SumAttributePathList,
                    attributePath => entityGrouping.Sum(x =>
                        ConvertValue(x.GetAttributeValueByAccessPath(_ckCacheService, _tenantId, attributePath))))
            );
            calculateGrouping.Add(grouping);
        }

        return calculateGrouping;
    }

    private decimal ConvertValue(object? value)
    {
        // Convert the value to decimal if it is null, return 0
        if (value == null)
        {
            return 0;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        if (value is double doubleValue)
        {
            return (decimal)doubleValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return longValue;
        }

        if (value is float floatValue)
        {
            return (decimal)floatValue;
        }

        if (value is string stringValue && decimal.TryParse(stringValue, out var parsedValue))
        {
            return parsedValue;
        }

        throw RuntimeRepositoryException.InvalidValueType(value, typeof(decimal));
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
                    AttributePath = attributeName,
                    Value = calcFunction(attributeName)
                });
            }
        }

        return list;
    }

    private class Key(IEnumerable<object?> keys) : IComparable
    {
        public IEnumerable<object?> Keys { get; } = keys;

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