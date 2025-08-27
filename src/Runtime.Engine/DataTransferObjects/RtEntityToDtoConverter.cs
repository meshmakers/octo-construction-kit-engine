using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.DataTransferObjects;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.DataTransferObjects;

/// <summary>
/// Converts RtEntity to RtEntityDto
/// </summary>
/// <param name="ckCacheService">Construction Kit Cache Service</param>
public class RtEntityToDtoConverter(ICkCacheService ckCacheService) : IRtEntityToDtoConverter
{
    /// <summary>
    /// Converts a RtEntity to RtEntityDto
    /// </summary>
    /// <param name="tenantId">Tenant Id</param>
    /// <param name="rtEntity">The RtEntity to convert</param>
    /// <returns></returns>
    /// <exception cref="PersistenceException">Thrown if the CkTypeId is undefined</exception>
    public RtEntityDto Convert(string tenantId, RtEntity rtEntity)
    {
        var ckTypeGraph =
            ckCacheService.GetCkType(tenantId, rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet());

        var entityDto = new RtEntityDto
        {
            RtId = rtEntity.RtId,
            RtChangedDateTime = rtEntity.RtChangedDateTime,
            RtCreationDateTime = rtEntity.RtCreationDateTime,
            RtWellKnownName = rtEntity.RtWellKnownName,
            CkTypeId = rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet()
        };

        ConvertAttributes(tenantId, ckTypeGraph, rtEntity, entityDto);

        return entityDto;
    }

    private void ConvertAttributes(string tenantId, CkTypeWithAttributesGraph ckTypeWithAttributesGraph, RtTypeWithAttributes rtTypeWithAttributes, RtTypeWithAttributesDto rtTypeWithAttributesDto)
    {
        rtTypeWithAttributesDto.Attributes.AddRange(rtTypeWithAttributes.Attributes.Select(pair =>
        {
            var typeAttributeGraph = ckTypeWithAttributesGraph.AllAttributesByName[pair.Key];

            var value = pair.Value;
            if (value is RtRecord rtRecord)
            {
                value = ConvertToRtRecordDto(tenantId, rtRecord);
            }
            else if (value is IEnumerable<object> rtRecords)
            {
                value = rtRecords.Select(listValue =>
                {
                    if (listValue is RtRecord rtRecord2)
                    {
                        return ConvertToRtRecordDto(tenantId, rtRecord2);
                    }

                    return listValue;
                });
            }

            return new RtAttributeDto
            {
                Id = typeAttributeGraph.CkAttributeId,
                Value = value
            };
        }));
    }

    private RtRecordDto ConvertToRtRecordDto(string tenantId, RtRecord rtRecord)
    {
        var rtRecordDto = new RtRecordDto
        {
            CkRecordId = rtRecord.CkRecordId
        };

        var ckRecordGraph = ckCacheService.GetCkRecord(tenantId, rtRecord.CkRecordId);
        ConvertAttributes(tenantId, ckRecordGraph, rtRecord, rtRecordDto);
        return rtRecordDto;
    }
}