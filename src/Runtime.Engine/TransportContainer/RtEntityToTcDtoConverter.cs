using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Engine.TransportContainer;

/// <summary>
/// Converts RtEntity to RtEntityDto
/// </summary>
/// <param name="ckCacheService">Construction Kit Cache Service</param>
public class RtEntityToTcDtoConverter(ICkCacheService ckCacheService) : IRtEntityToTcDtoConverter
{
    /// <inheritdoc />
    public RtEntityTcDto Convert(string tenantId, RtEntity rtEntity,
        AttributeValueResolveFlags attributeValueResolveFlags = AttributeValueResolveFlags.Default)
    {
        var ckTypeGraph =
            ckCacheService.GetRtCkType(tenantId, rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet());

        var entityDto = new RtEntityTcDto
        {
            RtId = rtEntity.RtId,
            RtChangedDateTime = rtEntity.RtChangedDateTime,
            RtCreationDateTime = rtEntity.RtCreationDateTime,
            RtWellKnownName = rtEntity.RtWellKnownName,
            CkTypeId = rtEntity.CkTypeId ?? throw PersistenceException.CkTypeIdNotSet()
        };

        ConvertAttributes(tenantId, ckTypeGraph, rtEntity, entityDto, attributeValueResolveFlags);

        return entityDto;
    }

    private void ConvertAttributes(string tenantId, CkTypeWithAttributesGraph ckTypeWithAttributesGraph,
        RtTypeWithAttributes rtTypeWithAttributes, RtTypeWithAttributesTcDto rtTypeWithAttributesDto,
        AttributeValueResolveFlags attributeValueResolveFlags)
    {
        foreach (var ckTypeAttributeGraph in ckTypeWithAttributesGraph.AllAttributesByName.Values)
        {
            if (!rtTypeWithAttributes.Attributes.TryGetValue(ckTypeAttributeGraph.AttributeName, out var value))
            {
                continue;
            }

            if (value is RtRecord rtRecord)
            {
                value = ConvertToRtRecordDto(tenantId, rtRecord, attributeValueResolveFlags);
            }
            else if (value is IEnumerable<object> rtRecords)
            {
                value = rtRecords.Select(listValue =>
                {
                    if (listValue is RtRecord rtRecord2)
                    {
                        return ConvertToRtRecordDto(tenantId, rtRecord2, attributeValueResolveFlags);
                    }

                    return listValue;
                });
            }
            else if (attributeValueResolveFlags.HasFlag(AttributeValueResolveFlags.ResolveEnumsToNames) &&
                     ckTypeAttributeGraph is { ValueType: AttributeValueTypesDto.Enum, ValueCkEnumId: not null } &&
                     value is int key)
            {
                var enumGraph = ckCacheService.GetCkEnum(tenantId, ckTypeAttributeGraph.ValueCkEnumId);
                var ckEnumValueDto = enumGraph.Values.FirstOrDefault(o => o.Key == key);
                if (ckEnumValueDto != null)
                {
                    value = ckEnumValueDto.Name;
                }
            }

            var rtEntityAttributeDto = new RtAttributeTcDto
            {
                Id = ckTypeAttributeGraph.CkAttributeId.ToRtCkId(),
                Value = value
            };
            rtTypeWithAttributesDto.Attributes.Add(rtEntityAttributeDto);
        }
    }

    private RtRecordTcDto ConvertToRtRecordDto(string tenantId, RtRecord rtRecord, AttributeValueResolveFlags attributeValueResolveFlags)
    {
        var rtRecordDto = new RtRecordTcDto
        {
            CkRecordId = rtRecord.CkRecordId
        };

        var ckRecordGraph = ckCacheService.GetRtCkRecord(tenantId, rtRecord.CkRecordId);
        ConvertAttributes(tenantId, ckRecordGraph, rtRecord, rtRecordDto, attributeValueResolveFlags);
        return rtRecordDto;
    }
}