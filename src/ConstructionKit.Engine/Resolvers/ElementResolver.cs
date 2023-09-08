using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Implementation of <see cref="IElementResolver"/> that resolves the elements of a compiled model.
/// </summary>
public class ElementResolver : IElementResolver
{
    /// <inheritdoc />
    public CkModelGraph Resolve(CkCompiledModelRoot ckCompiledModelRoot, OperationResult validationResult)
    {
        var ckModelGraph = new CkModelGraph();

        if (ckCompiledModelRoot.Attributes != null)
        {
            foreach (var ckAttribute in ckCompiledModelRoot.Attributes)
            {
                var ckAttributeId = new CkId<CkAttributeId>(ckCompiledModelRoot.ModelId, ckAttribute.AttributeId);

                if (!Regex.IsMatch(ckAttribute.AttributeId.AttributeId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    validationResult.AddMessage(MessageCodes.CkAttributeIdContainsInvalidCharacters(ckAttribute.AttributeId.AttributeId));
                    continue;
                }

                if (ckModelGraph.Attributes.ContainsKey(ckAttributeId))
                {
                    validationResult.AddMessage(MessageCodes.AttributeIdNotUnique(ckAttributeId));
                    continue;
                }

                if ((ckAttribute.ValueType == AttributeValueTypesDto.Record || ckAttribute.ValueType == AttributeValueTypesDto.RecordArray)
                    && ckAttribute.ValueCkRecordId == null)
                {
                    validationResult.AddMessage(MessageCodes.CkRecordIdUndefined(ckAttributeId));
                    continue;
                }
                
                if (ckAttribute.ValueType == AttributeValueTypesDto.Enum && ckAttribute.ValueCkEnumId == null)
                {
                    validationResult.AddMessage(MessageCodes.CkEnumIdUndefined(ckAttributeId));
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
                    validationResult.AddMessage(
                        MessageCodes.CkAssociationIdContainsInvalidCharacters(ckAssociationRole.AssociationRoleId.RoleId));
                    continue;
                }

                if (ckModelGraph.AssociationRoles.ContainsKey(ckAssociationId))
                {
                    validationResult.AddMessage(MessageCodes.AssociationRoleIdNotUnique(ckAssociationId));
                    continue;
                }

                ckModelGraph.GetOrCreateAssociationRoles(ckAssociationId, ckAssociationRole);
            }
        }

        if (ckCompiledModelRoot.Types != null)
        {
            foreach (var ckType in ckCompiledModelRoot.Types)
            {
                var ckTypeId = new CkId<CkTypeId>(ckCompiledModelRoot.ModelId, ckType.TypeId);
                if (!Regex.IsMatch(ckType.TypeId.TypeId, CompilerStatics.AllowedCharactersInNamesRegex))
                {
                    validationResult.AddMessage(MessageCodes.CkTypeIdContainsInvalidCharacters(ckType.TypeId.TypeId));
                    continue;
                }

                if (ckModelGraph.Types.ContainsKey(ckTypeId))
                {
                    validationResult.AddMessage(MessageCodes.TypeIdNotUnique(ckTypeId));
                    continue;
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
                    validationResult.AddMessage(MessageCodes.CkRecordIdContainsInvalidCharacters(ckRecord.RecordId.RecordId));
                    continue;
                }

                if (ckModelGraph.Records.ContainsKey(ckRecordId))
                {
                    validationResult.AddMessage(MessageCodes.RecordIdNotUnique(ckRecordId));
                    continue;
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
                    validationResult.AddMessage(MessageCodes.CkEnumIdContainsInvalidCharacters(ckEnum.EnumId.EnumId));
                    continue;
                }

                if (ckModelGraph.Enums.ContainsKey(ckEnumId))
                {
                    validationResult.AddMessage(MessageCodes.EnumIdNotUnique(ckEnumId));
                    continue;
                }

                bool ignoreEnum = false;
                foreach (var ckSelectionValueGroup in
                         ckEnum.Values.GroupBy(x => x.Key).Where(x => x.Count() > 1))
                {
                    validationResult.AddMessage(MessageCodes.SelectionValueNotUnique(ckEnumId, ckSelectionValueGroup.Key));
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