using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     CK v2 visibility rules on the whole graph after reference resolution — at compile time and on every import
///     (<c>HardResolveAsync</c> runs the same resolvers), so a forged or old-compiler model cannot bypass them. Unknown
///     references are skipped here (reported by the reference rules). v1 models are all Public / Any.
///     <list type="bullet">
///         <item>
///             112 (F1.2-S3, AB#5912, concept §4.2): an element with <c>visibility: Internal</c> cannot be referenced
///             from another model.
///         </item>
///         <item>113: a type or record with <c>derivable: Model</c> cannot be derived from in another model.</item>
///         <item>
///             129 (AB#6334 / AB#6335, F2.1 gate findings H1/H3): inconsistent visibility — a <b>public</b> element
///             cannot reference an <b>internal</b> element of its own model, and a public interface cannot declare an
///             internal method. Otherwise a breaking change to the internal element would be capped at Minor by the
///             compatibility classifier (rows N1–N5) while dependents reach it through the public element. The
///             redeclaration of a public interface method as internal is checked in
///             <c>InheritanceResolver.InheritInterfaceMethods</c>.
///         </item>
///     </list>
///     The reference walk below is the single list of CK reference properties checked for visibility;
///     <c>CkVisibilityReferenceCoverageTests</c> fails when a CK DTO gains a <c>CkId&lt;…&gt;</c> reference that is
///     neither walked here nor listed there with a reason.
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
            var referrer = new Referrer(modelGraph, owner.ModelId, owner, type.Visibility, location, operationResult);

            if (type.DerivedFromCkTypeId is { } baseId && modelGraph.Types.TryGetValue(baseId, out var baseType))
            {
                if (IsForeign(owner.ModelId, baseId.ModelId))
                {
                    if (baseType.Visibility == CkVisibilityDto.Internal)
                    {
                        referrer.ReportForeign("type", baseId);
                    }
                    else if (baseType.Derivable == CkDerivableDto.Model)
                    {
                        operationResult.AddMessage(MessageCodes.CkElementNotDerivable(location, "Type", owner, baseId));
                    }
                }
                else
                {
                    referrer.CheckOwn(baseType.Visibility, "base type", baseId);
                }
            }

            CheckAttributes(referrer, type.DefinedAttributes);

            foreach (var implemented in type.DeclaredImplements)
            {
                CheckInterface(referrer, implemented, "implemented interface");
            }

            foreach (var association in type.Associations.DefinedAssociations)
            {
                CheckRole(referrer, association.CkRoleId);
                CheckType(referrer, association.TargetCkTypeId, "association target type");
                CheckInterface(referrer, association.TargetCkInterfaceId, "association target interface");
                foreach (var targetAttribute in association.TargetCkAttributeIds ?? [])
                {
                    CheckAttribute(referrer, targetAttribute, "association target attribute");
                }
            }

            CheckMethods(referrer, type.DefinedMethods, false);
        }

        foreach (var record in modelGraph.Records.Values)
        {
            var owner = record.CkRecordId;
            var location = originFileResolver.Resolve(owner);
            var referrer = new Referrer(modelGraph, owner.ModelId, owner, record.Visibility, location,
                operationResult);

            if (record.DerivedFromCkRecordId is { } baseId &&
                modelGraph.Records.TryGetValue(baseId, out var baseRecord))
            {
                if (IsForeign(owner.ModelId, baseId.ModelId))
                {
                    if (baseRecord.Visibility == CkVisibilityDto.Internal)
                    {
                        referrer.ReportForeign("record", baseId);
                    }
                    else if (baseRecord.Derivable == CkDerivableDto.Model)
                    {
                        operationResult.AddMessage(
                            MessageCodes.CkElementNotDerivable(location, "Record", owner, baseId));
                    }
                }
                else
                {
                    referrer.CheckOwn(baseRecord.Visibility, "base record", baseId);
                }
            }

            CheckAttributes(referrer, record.DefinedAttributes);
        }

        foreach (var role in modelGraph.AssociationRoles.Values)
        {
            var owner = role.CkRoleId;
            CheckAttributes(new Referrer(modelGraph, owner.ModelId, owner, role.Visibility,
                originFileResolver.Resolve(owner), operationResult), role.DefinedAttributes);
        }

        foreach (var attribute in modelGraph.Attributes.Values)
        {
            var owner = attribute.CkAttributeId;
            var referrer = new Referrer(modelGraph, owner.ModelId, owner, attribute.Visibility,
                originFileResolver.Resolve(owner), operationResult);
            CheckRecord(referrer, attribute.ValueCkRecordId, "value record");
            CheckEnum(referrer, attribute.ValueCkEnumId, "value enum");
        }

        foreach (var ckInterface in modelGraph.Interfaces.Values)
        {
            var owner = ckInterface.CkInterfaceId;
            var referrer = new Referrer(modelGraph, owner.ModelId, owner, ckInterface.Visibility,
                originFileResolver.Resolve(owner), operationResult);

            foreach (var member in ckInterface.DefinedAttributes)
            {
                CheckAttribute(referrer, member.CkAttributeId, "member attribute");
            }

            foreach (var extended in ckInterface.DeclaredExtends)
            {
                CheckInterface(referrer, extended, "extended interface");
            }

            foreach (var association in ckInterface.DefinedAssociations.Select(a => a.Definition))
            {
                CheckRole(referrer, association.CkRoleId);
                CheckType(referrer, association.TargetCkTypeId, "association target type");
                CheckInterface(referrer, association.TargetCkInterfaceId, "association target interface");
            }

            CheckMethods(referrer, ckInterface.DefinedMethods, true);
        }
    }

    /// <summary>
    ///     Another model, compared by name (versions of one model are the same model).
    /// </summary>
    private static bool IsForeign(CkModelId owner, CkModelId referenced) =>
        !string.Equals(owner.Name, referenced.Name, StringComparison.Ordinal);

    private static void CheckAttributes(Referrer referrer, IEnumerable<CkTypeAttributeDto> assignments)
    {
        foreach (var assignment in assignments)
        {
            CheckAttribute(referrer, assignment.CkAttributeId, "attribute");
        }
    }

    private static void CheckAttribute(Referrer referrer, CkId<CkAttributeId>? id, string kind)
    {
        if (id != null && referrer.Graph.Attributes.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "attribute", kind, id);
        }
    }

    private static void CheckType(Referrer referrer, CkId<CkTypeId>? id, string kind)
    {
        if (id != null && referrer.Graph.Types.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "type", kind, id);
        }
    }

    private static void CheckRecord(Referrer referrer, CkId<CkRecordId>? id, string kind)
    {
        if (id != null && referrer.Graph.Records.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "record", kind, id);
        }
    }

    private static void CheckEnum(Referrer referrer, CkId<CkEnumId>? id, string kind)
    {
        if (id != null && referrer.Graph.Enums.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "enum", kind, id);
        }
    }

    private static void CheckRole(Referrer referrer, CkId<CkAssociationRoleId>? id)
    {
        if (id != null && referrer.Graph.AssociationRoles.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "association role", "association role", id);
        }
    }

    private static void CheckInterface(Referrer referrer, CkId<CkInterfaceId>? id, string kind)
    {
        if (id != null && referrer.Graph.Interfaces.TryGetValue(id, out var graph))
        {
            referrer.Check(id.ModelId, graph.Visibility, "interface", kind, id);
        }
    }

    /// <summary>
    ///     Parameter and result records/enums of the methods. An internal method of a public type is an internal
    ///     element (row N) and may use internal records/enums; an internal method on a public interface is itself an
    ///     inconsistency (AB#6335).
    /// </summary>
    private static void CheckMethods(Referrer referrer, IEnumerable<CkMethodDto> methods, bool onInterface)
    {
        foreach (var method in methods)
        {
            var methodVisibility = CkModifiers.ResolveVisibility(method.Visibility);
            if (onInterface && referrer.IsPublic && methodVisibility == CkVisibilityDto.Internal)
            {
                referrer.Report($"is a public interface and declares the internal method '{method.MethodId}' — every " +
                                "implementor must provide it and callers reach it through the interface; make the " +
                                "method public or the interface internal");
            }

            var methodReferrer = referrer.ForMethod(method.MethodId, methodVisibility);
            foreach (var parameter in method.Parameters ?? [])
            {
                CheckRecord(methodReferrer, parameter.ValueCkRecordId, $"record of parameter '{parameter.Name}'");
                CheckEnum(methodReferrer, parameter.ValueCkEnumId, $"enum of parameter '{parameter.Name}'");
            }

            CheckRecord(methodReferrer, method.Result?.ValueCkRecordId, "result record");
            CheckEnum(methodReferrer, method.Result?.ValueCkEnumId, "result enum");
        }
    }

    /// <summary>
    ///     The element whose references are walked: reports 112 for an internal element of another model and 129 for
    ///     an internal element of its own model when the referrer itself is public.
    /// </summary>
    private sealed record Referrer(
        CkModelGraph Graph,
        CkModelId OwnerModel,
        object Owner,
        CkVisibilityDto Visibility,
        string? Location,
        OperationResult OperationResult)
    {
        /// <summary>
        ///     129 applies to ckLanguage 2 models only: a v1 model has no visibility (90 reports any declaration).
        /// </summary>
        public bool IsPublic => Visibility == CkVisibilityDto.Public &&
                                Graph.Models.TryGetValue(OwnerModel, out var model) && model.EffectiveCkLanguage >= 2;

        public Referrer ForMethod(string methodId, CkVisibilityDto methodVisibility) =>
            this with
            {
                Owner = $"{Owner}.{methodId}",
                Visibility = IsPublic && methodVisibility == CkVisibilityDto.Public
                    ? CkVisibilityDto.Public
                    : CkVisibilityDto.Internal
            };

        public void Check(CkModelId referencedModel, CkVisibilityDto referencedVisibility, string elementKind,
            string referenceKind, object referenced)
        {
            if (referencedVisibility != CkVisibilityDto.Internal)
            {
                return;
            }

            if (IsForeign(OwnerModel, referencedModel))
            {
                ReportForeign(elementKind, referenced);
            }
            else if (IsPublic)
            {
                Report($"is public and references the internal {elementKind} '{referenced}' ({referenceKind}) — make " +
                       $"'{referenced}' public or '{Owner}' internal");
            }
        }

        public void CheckOwn(CkVisibilityDto referencedVisibility, string referenceKind, object referenced)
        {
            if (IsPublic && referencedVisibility == CkVisibilityDto.Internal)
            {
                Report($"is public and references the internal {referenceKind.Split(' ').Last()} '{referenced}' " +
                       $"({referenceKind}) — make '{referenced}' public or '{Owner}' internal");
            }
        }

        public void ReportForeign(string kind, object referenced) =>
            OperationResult.AddMessage(MessageCodes.CkReferenceToInternalElement(Location, Owner, kind, referenced));

        public void Report(string reason) =>
            OperationResult.AddMessage(MessageCodes.CkInconsistentVisibility(Location, Owner, reason));
    }
}
