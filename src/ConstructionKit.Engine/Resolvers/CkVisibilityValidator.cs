using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     CK v2 (F1.2-S3, AB#5912, concept §4.2): an element with <c>visibility: Internal</c> cannot be referenced from
///     another model (112), and a type or record with <c>derivable: Model</c> cannot be derived from in another
///     model (113). Runs on the whole graph after reference resolution — at compile time and on every import
///     (<c>HardResolveAsync</c> runs the same resolvers), so a forged or old-compiler model cannot bypass it. Unknown
///     references are skipped here (reported by the reference rules). v1 models are all Public / Any.
/// </summary>
internal static class CkVisibilityValidator
{
    public static void Validate(CkModelGraph modelGraph, IOriginFileResolver originFileResolver,
        OperationResult operationResult)
    {
        foreach (var type in modelGraph.Types.Values)
        {
            var owner = type.CkTypeId;
            var location = originFileResolver.Resolve(owner);
            var ownerModel = owner.ModelId;

            void Report(string kind, object referenced) =>
                operationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(location, owner, kind, referenced));

            if (type.DerivedFromCkTypeId is { } baseId && IsForeign(ownerModel, baseId.ModelId) &&
                modelGraph.Types.TryGetValue(baseId, out var baseType))
            {
                if (baseType.Visibility == CkVisibilityDto.Internal)
                {
                    Report("type", baseId);
                }
                else if (baseType.Derivable == CkDerivableDto.Model)
                {
                    operationResult.AddMessage(MessageCodes.CkElementNotDerivable(location, "Type", owner, baseId));
                }
            }

            CheckAttributes(modelGraph, ownerModel, type.DefinedAttributes, Report);

            foreach (var implemented in type.DeclaredImplements)
            {
                CheckInterface(modelGraph, ownerModel, implemented, Report);
            }

            foreach (var association in type.Associations.DefinedAssociations)
            {
                CheckRole(modelGraph, ownerModel, association.CkRoleId, Report);
                CheckType(modelGraph, ownerModel, association.TargetCkTypeId, Report);
                CheckInterface(modelGraph, ownerModel, association.TargetCkInterfaceId, Report);
            }

            CheckMethods(modelGraph, ownerModel, type.DefinedMethods, Report);
        }

        foreach (var record in modelGraph.Records.Values)
        {
            var owner = record.CkRecordId;
            var location = originFileResolver.Resolve(owner);

            void Report(string kind, object referenced) =>
                operationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(location, owner, kind, referenced));

            if (record.DerivedFromCkRecordId is { } baseId && IsForeign(owner.ModelId, baseId.ModelId) &&
                modelGraph.Records.TryGetValue(baseId, out var baseRecord))
            {
                if (baseRecord.Visibility == CkVisibilityDto.Internal)
                {
                    Report("record", baseId);
                }
                else if (baseRecord.Derivable == CkDerivableDto.Model)
                {
                    operationResult.AddMessage(MessageCodes.CkElementNotDerivable(location, "Record", owner, baseId));
                }
            }

            CheckAttributes(modelGraph, owner.ModelId, record.DefinedAttributes, Report);
        }

        foreach (var role in modelGraph.AssociationRoles.Values)
        {
            var owner = role.CkRoleId;
            var location = originFileResolver.Resolve(owner);
            CheckAttributes(modelGraph, owner.ModelId, role.DefinedAttributes, (kind, referenced) =>
                operationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(location, owner, kind, referenced)));
        }

        foreach (var attribute in modelGraph.Attributes.Values)
        {
            var owner = attribute.CkAttributeId;
            var location = originFileResolver.Resolve(owner);

            void Report(string kind, object referenced) =>
                operationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(location, owner, kind, referenced));

            CheckRecord(modelGraph, owner.ModelId, attribute.ValueCkRecordId, Report);
            CheckEnum(modelGraph, owner.ModelId, attribute.ValueCkEnumId, Report);
        }

        foreach (var ckInterface in modelGraph.Interfaces.Values)
        {
            var owner = ckInterface.CkInterfaceId;
            var location = originFileResolver.Resolve(owner);
            var ownerModel = owner.ModelId;

            void Report(string kind, object referenced) =>
                operationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(location, owner, kind, referenced));

            foreach (var member in ckInterface.DefinedAttributes)
            {
                CheckAttribute(modelGraph, ownerModel, member.CkAttributeId, Report);
            }

            foreach (var extended in ckInterface.DeclaredExtends)
            {
                CheckInterface(modelGraph, ownerModel, extended, Report);
            }

            foreach (var association in ckInterface.DefinedAssociations.Select(a => a.Definition))
            {
                CheckRole(modelGraph, ownerModel, association.CkRoleId, Report);
                CheckType(modelGraph, ownerModel, association.TargetCkTypeId, Report);
                CheckInterface(modelGraph, ownerModel, association.TargetCkInterfaceId, Report);
            }

            CheckMethods(modelGraph, ownerModel, ckInterface.DefinedMethods, Report);
        }
    }

    /// <summary>
    ///     Another model, compared by name (versions of one model are the same model).
    /// </summary>
    private static bool IsForeign(CkModelId owner, CkModelId referenced) =>
        !string.Equals(owner.Name, referenced.Name, StringComparison.Ordinal);

    private static void CheckAttributes(CkModelGraph modelGraph, CkModelId ownerModel,
        IEnumerable<CkTypeAttributeDto> assignments, Action<string, object> report)
    {
        foreach (var assignment in assignments)
        {
            CheckAttribute(modelGraph, ownerModel, assignment.CkAttributeId, report);
        }
    }

    private static void CheckAttribute(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkAttributeId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) && modelGraph.Attributes.TryGetValue(id, out var graph) &&
            graph.Visibility == CkVisibilityDto.Internal)
        {
            report("attribute", id);
        }
    }

    private static void CheckType(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkTypeId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) && modelGraph.Types.TryGetValue(id, out var graph) &&
            graph.Visibility == CkVisibilityDto.Internal)
        {
            report("type", id);
        }
    }

    private static void CheckRecord(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkRecordId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) && modelGraph.Records.TryGetValue(id, out var graph) &&
            graph.Visibility == CkVisibilityDto.Internal)
        {
            report("record", id);
        }
    }

    private static void CheckEnum(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkEnumId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) && modelGraph.Enums.TryGetValue(id, out var graph) &&
            graph.Visibility == CkVisibilityDto.Internal)
        {
            report("enum", id);
        }
    }

    private static void CheckRole(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkAssociationRoleId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) &&
            modelGraph.AssociationRoles.TryGetValue(id, out var graph) && graph.Visibility == CkVisibilityDto.Internal)
        {
            report("association role", id);
        }
    }

    private static void CheckInterface(CkModelGraph modelGraph, CkModelId ownerModel, CkId<CkInterfaceId>? id,
        Action<string, object> report)
    {
        if (id != null && IsForeign(ownerModel, id.ModelId) && modelGraph.Interfaces.TryGetValue(id, out var graph) &&
            graph.Visibility == CkVisibilityDto.Internal)
        {
            report("interface", id);
        }
    }

    private static void CheckMethods(CkModelGraph modelGraph, CkModelId ownerModel,
        IEnumerable<CkMethodDto> methods, Action<string, object> report)
    {
        foreach (var method in methods)
        {
            foreach (var parameter in method.Parameters ?? [])
            {
                CheckRecord(modelGraph, ownerModel, parameter.ValueCkRecordId, report);
                CheckEnum(modelGraph, ownerModel, parameter.ValueCkEnumId, report);
            }

            CheckRecord(modelGraph, ownerModel, method.Result?.ValueCkRecordId, report);
            CheckEnum(modelGraph, ownerModel, method.Result?.ValueCkEnumId, report);
        }
    }
}
