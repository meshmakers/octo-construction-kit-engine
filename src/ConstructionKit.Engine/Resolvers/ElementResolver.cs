using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Implementation of <see cref="IElementResolver" /> that resolves the elements of a compiled model.
/// </summary>
internal class ElementResolver : IElementResolver
{
    /// <inheritdoc />
    public CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, IVariableResolver variableResolver, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        var ckModelGraph = new CkModelGraph();
        ckModelGraph.GetOrCreateModel(ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Description);

        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                var ckAttributeId = new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId);

                if (!Regex.IsMatch(ckAttribute.AttributeId.AttributeId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdContainsInvalidCharacters(originFileResolver.Resolve(ckAttributeId),
                        ckAttribute.AttributeId.AttributeId));
                    continue;
                }

                if (ckModelGraph.Attributes.ContainsKey(ckAttributeId))
                {
                    operationResult.AddMessage(MessageCodes.AttributeIdNotUnique(originFileResolver.Resolve(ckAttributeId), ckAttributeId));
                    continue;
                }

                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record || ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId == null)
                {
                    operationResult.AddMessage(MessageCodes.CkRecordIdUndefined(originFileResolver.Resolve(ckAttributeId), ckAttributeId));
                    continue;
                }
                
                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record || ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId != null)
                {
                    ckAttribute.ValueCkRecordId = variableResolver.Resolve(ckAttribute.ValueCkRecordId.ToString());
                }

                if (ckAttribute is { ValueType: AttributeValueTypesDto.Enum, ValueCkEnumId: null })
                {
                    operationResult.AddMessage(MessageCodes.CkEnumIdUndefined(originFileResolver.Resolve(ckAttributeId), ckAttributeId));
                    continue;
                }
                
                if (ckAttribute is { ValueType: AttributeValueTypesDto.Enum, ValueCkEnumId: not null })
                {
                    ckAttribute.ValueCkEnumId = variableResolver.Resolve(ckAttribute.ValueCkEnumId.ToString());
                }

                ckModelGraph.GetOrCreateAttribute(ckAttributeId, ckAttribute);
            }
        }

        if (ckCompiledModelRoot.AssociationRoles != null)
        {
            foreach (var ckAssociationRole in ckCompiledModelRoot.AssociationRoles)
            {
                var ckAssociationId = new CkId<CkAssociationRoleId>(ckCompiledModelRoot.ModelId, ckAssociationRole.AssociationRoleId);
                if (!Regex.IsMatch(ckAssociationRole.AssociationRoleId.RoleId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(
                        MessageCodes.CkAssociationIdContainsInvalidCharacters(originFileResolver.Resolve(ckAssociationId),
                            ckAssociationRole.AssociationRoleId.RoleId));
                    continue;
                }

                if (ckModelGraph.AssociationRoles.ContainsKey(ckAssociationId))
                {
                    operationResult.AddMessage(MessageCodes.AssociationRoleIdNotUnique(originFileResolver.Resolve(ckAssociationId), ckAssociationId));
                    continue;
                }

                if (ckAssociationRole.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute ids
                    var duplicateAttributeIds = ckAssociationRole.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkAssociationRoleAttributeIdNotUnique(originFileResolver.Resolve(ckAssociationId), ckAssociationRole.AssociationRoleId,
                                string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckAssociationRole.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkAssociationRoleAttributeNameNotUnique(originFileResolver.Resolve(ckAssociationId), ckAssociationRole.AssociationRoleId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }
                    
                    foreach (var ckTypeAttributeDto in ckAssociationRole.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId = variableResolver.Resolve(ckTypeAttributeDto.CkAttributeId.ToString());
                    }
                }

                ckModelGraph.GetOrCreateAssociationRole(ckAssociationId, ckAssociationRole);
            }
        }

        if (ckCompiledModelRoot.Types != null)
        {
            foreach (var ckType in ckCompiledModelRoot.Types)
            {
                var ckTypeId = new CkId<CkTypeId>(ckCompiledModelRoot.ModelId, ckType.TypeId);
                if (!Regex.IsMatch(ckType.TypeId.TypeId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkTypeIdContainsInvalidCharacters(originFileResolver.Resolve(ckTypeId), ckType.TypeId.TypeId));
                    continue;
                }

                if (ckModelGraph.Types.ContainsKey(ckTypeId))
                {
                    operationResult.AddMessage(MessageCodes.TypeIdNotUnique(originFileResolver.Resolve(ckTypeId), ckTypeId));
                    continue;
                }

                if (ckType.DerivedFromCkTypeId != null)
                {
                    ckType.DerivedFromCkTypeId = variableResolver.Resolve(ckType.DerivedFromCkTypeId.ToString());
                }

                if (ckType.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkType) have duplicate attribute ids
                    var duplicateAttributeIds = ckType.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeIdNotUnique(originFileResolver.Resolve(ckTypeId), ckType.TypeId, 
                                string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkType) have duplicate attribute names
                    var duplicateAttributeNames = ckType.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeNameNotUnique(originFileResolver.Resolve(ckTypeId), ckType.TypeId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }

                    foreach (var ckTypeAttributeDto in ckType.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId = variableResolver.Resolve(ckTypeAttributeDto.CkAttributeId.ToString());
                    }
                }

                if (ckType.Associations != null)
                {
                    foreach (var ckTypeAssociationDto in ckType.Associations)
                    {
                        ckTypeAssociationDto.CkRoleId = variableResolver.Resolve(ckTypeAssociationDto.CkRoleId.ToString());
                        ckTypeAssociationDto.TargetCkTypeId = variableResolver.Resolve(ckTypeAssociationDto.TargetCkTypeId.ToString());
                    }
                }

                ckModelGraph.GetOrCreateType(ckTypeId, ckType);
            }
        }

        if (ckCompiledModelRoot.Records != null)
        {
            foreach (var ckRecord in ckCompiledModelRoot.Records)
            {
                var ckRecordId = new CkId<CkRecordId>(ckCompiledModelRoot.ModelId, ckRecord.RecordId);
                if (!Regex.IsMatch(ckRecord.RecordId.RecordId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkRecordIdContainsInvalidCharacters(originFileResolver.Resolve(ckRecordId),  
                        ckRecord.RecordId.RecordId));
                    continue;
                }

                if (ckModelGraph.Records.ContainsKey(ckRecordId))
                {
                    operationResult.AddMessage(MessageCodes.RecordIdNotUnique(originFileResolver.Resolve(ckRecordId), ckRecordId));
                    continue;
                }
                
                if (ckRecord.DerivedFromCkRecordId != null)
                {
                    ckRecord.DerivedFromCkRecordId = variableResolver.Resolve(ckRecord.DerivedFromCkRecordId.ToString());
                }

                if (ckRecord.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute ids
                    var duplicateAttributeIds = ckRecord.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeIdNotUnique(originFileResolver.Resolve(ckRecordId), ckRecord.RecordId, string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckRecord.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeNameNotUnique(originFileResolver.Resolve(ckRecordId), ckRecord.RecordId,
                                string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
                    }
                    
                    foreach (var ckTypeAttributeDto in ckRecord.Attributes)
                    {
                        ckTypeAttributeDto.CkAttributeId = variableResolver.Resolve(ckTypeAttributeDto.CkAttributeId.ToString());
                    }
                }

                ckModelGraph.GetOrCreateRecord(ckRecordId, ckRecord);
            }
        }

        if (ckCompiledModelRoot.Enums != null)
        {
            foreach (var ckEnum in ckCompiledModelRoot.Enums)
            {
                var ckEnumId = new CkId<CkEnumId>(ckCompiledModelRoot.ModelId, ckEnum.EnumId);
                if (!Regex.IsMatch(ckEnum.EnumId.EnumId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkEnumIdContainsInvalidCharacters(originFileResolver.Resolve(ckEnumId), ckEnum.EnumId.EnumId));
                    continue;
                }

                if (ckModelGraph.Enums.ContainsKey(ckEnumId))
                {
                    operationResult.AddMessage(MessageCodes.EnumIdNotUnique(originFileResolver.Resolve(ckEnumId), ckEnumId));
                    continue;
                }

                var ignoreEnum = false;
                foreach (var ckEnumValueDto in ckEnum.Values)
                {
                    if (string.IsNullOrWhiteSpace(ckEnumValueDto.Name))
                    {
                        operationResult.AddMessage(MessageCodes.EnumNameMyNotBeEmpty(originFileResolver.Resolve(ckEnumId), ckEnumId, ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }
                    
                    if (!Regex.IsMatch(ckEnumValueDto.Name, CompilerStatics.AllowedCharactersInEnumNamesRegex))
                    {
                        operationResult.AddMessage(MessageCodes.EnumNameMayNotContainWhitespaceSpecialCharacters(originFileResolver.Resolve(ckEnumId), ckEnumId, ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }

                    if (ckEnumValueDto.Key < 0)
                    {
                        operationResult.AddMessage(MessageCodes.EnumKeyMayNotBeNegative(originFileResolver.Resolve(ckEnumId), ckEnumId, ckEnumValueDto.Key));
                        ignoreEnum = true;
                    }
                }
                
                foreach (var ckSelectionValueGroup in
                         ckEnum.Values.GroupBy(x => x.Key).Where(x => x.Count() > 1))
                {
                    operationResult.AddMessage(MessageCodes.SelectionValueNotUnique(originFileResolver.Resolve(ckEnumId), ckEnumId, ckSelectionValueGroup.Key));
                    ignoreEnum = true;
                }

                if (!ckEnum.IsExtensible && ckEnum.Values.Any(x => x.IsExtension))
                {
                    operationResult.AddMessage(MessageCodes.EnumIsNotExtensibleButContainsExtension(originFileResolver.Resolve(ckEnumId), ckEnumId));
                    ignoreEnum = true;
                }

                if (ignoreEnum)
                {
                    continue;
                }

                ckModelGraph.GetOrCreateEnum(ckEnumId, ckEnum);
            }
        }

        return ckModelGraph;
    }
}