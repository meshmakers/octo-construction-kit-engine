using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Implementation of services that provides the corresponding class type for a given CkTypeId
/// </summary>
public class CkClassMappingService : ICkClassMappingService
{
    private readonly IEnumerable<ICkClassMap> _ckClassMaps;

    /// <summary>
    /// Creates a new instance of the service
    /// </summary>
    /// <param name="ckClassMaps">A list of all available type class maps</param>
    public CkClassMappingService(IEnumerable<ICkClassMap> ckClassMaps)
    {
        _ckClassMaps = ckClassMaps;
    }

    /// <inheritdoc />
    public Type? GetCkTypeClass(CkId<CkTypeId> ckTypeId)
    {
        var map = _ckClassMaps.FirstOrDefault(m => m.ModelId == ckTypeId.ModelId);

        return map?.GetCkTypeClass(ckTypeId.Key);
    }

    /// <inheritdoc />
    public Type? GetCkRecordClass(CkId<CkRecordId> ckRecordId)
    {
        var map = _ckClassMaps.FirstOrDefault(m => m.ModelId == ckRecordId.ModelId);

        return map?.GetCkRecordClass(ckRecordId.Key);
    }
}