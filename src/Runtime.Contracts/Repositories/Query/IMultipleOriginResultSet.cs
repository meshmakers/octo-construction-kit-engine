using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents a result set that contains multiple origins.
/// </summary>
/// <typeparam name="TChildEntity">Type of child entity</typeparam>
public interface IMultipleOriginResultSet<TChildEntity> : IDictionary<RtEntityId, IResultSet<TChildEntity>>;