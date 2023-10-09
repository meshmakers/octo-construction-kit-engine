using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Implementation of <see cref="IElementResolver"/> that resolves the elements of a compiled model.
/// </summary>
internal class ElementResolver : IElementResolver
{
    /// <inheritdoc />
    public CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, OperationResult operationResult)
    {
        var ckModelGraph = new CkModelGraph();

        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                var ckAttributeId = new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId);

                if (!Regex.IsMatch(ckAttribute.AttributeId.AttributeId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    operationResult.AddMessage(MessageCodes.CkAttributeIdContainsInvalidCharacters(ckAttribute.AttributeId.AttributeId));
                    continue;
                }

                if (ckModelGraph.Attributes.ContainsKey(ckAttributeId))
                {
                    operationResult.AddMessage(MessageCodes.AttributeIdNotUnique(ckAttributeId));
                    continue;
                }

                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record || ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId == null)
                {
                    operationResult.AddMessage(MessageCodes.CkRecordIdUndefined(ckAttributeId));
                    continue;
                }
                
                if (ckAttribute.ValueType == AttributeValueTypesDto.Enum && ckAttribute.ValueCkEnumId == null)
                {
                    operationResult.AddMessage(MessageCodes.CkEnumIdUndefined(ckAttributeId));
                    continue;
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
                        MessageCodes.CkAssociationIdContainsInvalidCharacters(ckAssociationRole.AssociationRoleId.RoleId));
                    continue;
                }

                if (ckModelGraph.AssociationRoles.ContainsKey(ckAssociationId))
                {
                    operationResult.AddMessage(MessageCodes.AssociationRoleIdNotUnique(ckAssociationId));
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
                            MessageCodes.CkAssociationRoleAttributeIdNotUnique(ckAssociationRole.AssociationRoleId, string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckAssociationRole.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkAssociationRoleAttributeNameNotUnique(ckAssociationRole.AssociationRoleId, string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
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
                    operationResult.AddMessage(MessageCodes.CkTypeIdContainsInvalidCharacters(ckType.TypeId.TypeId));
                    continue;
                }

                if (ckModelGraph.Types.ContainsKey(ckTypeId))
                {
                    operationResult.AddMessage(MessageCodes.TypeIdNotUnique(ckTypeId));
                    continue;
                }

                if (ckType.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkType) have duplicate attribute ids
                    var duplicateAttributeIds = ckType.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeIdNotUnique(ckType.TypeId, string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkType) have duplicate attribute names
                    var duplicateAttributeNames = ckType.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkTypeIdAttributeNameNotUnique(ckType.TypeId, string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
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
                    operationResult.AddMessage(MessageCodes.CkRecordIdContainsInvalidCharacters(ckRecord.RecordId.RecordId));
                    continue;
                }

                if (ckModelGraph.Records.ContainsKey(ckRecordId))
                {
                    operationResult.AddMessage(MessageCodes.RecordIdNotUnique(ckRecordId));
                    continue;
                }
                
                if (ckRecord.Attributes != null)
                {
                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute ids
                    var duplicateAttributeIds = ckRecord.Attributes.GroupBy(x => x.CkAttributeId)
                        .Where(x => x.Count() > 1).Select(x => x.Key).ToList();
                    if (duplicateAttributeIds.Any())
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeIdNotUnique(ckRecord.RecordId, string.Join(", ", duplicateAttributeIds)));
                        continue;
                    }

                    // Check if the defined attributes (=defined at CkRecord) have duplicate attribute names
                    var duplicateAttributeNames = ckRecord.Attributes.GroupBy(a => a.AttributeName)
                        .Where(a => a.Count() > 1).ToList();
                    if (duplicateAttributeNames.Count > 0)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkRecordIdAttributeNameNotUnique(ckRecord.RecordId, string.Join(", ", duplicateAttributeNames.Select(a => a.Key))));
                        continue;
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
                    operationResult.AddMessage(MessageCodes.CkEnumIdContainsInvalidCharacters(ckEnum.EnumId.EnumId));
                    continue;
                }

                if (ckModelGraph.Enums.ContainsKey(ckEnumId))
                {
                    operationResult.AddMessage(MessageCodes.EnumIdNotUnique(ckEnumId));
                    continue;
                }

                bool ignoreEnum = false;
                foreach (var ckSelectionValueGroup in
                         ckEnum.Values.GroupBy(x => x.Key).Where(x => x.Count() > 1))
                {
                    operationResult.AddMessage(MessageCodes.SelectionValueNotUnique(ckEnumId, ckSelectionValueGroup.Key));
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