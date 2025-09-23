using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
/// Interface for a construction kit model graph.
/// </summary>
public interface ICkModelGraph
{
    /// <summary>
    ///     Returns the types of the model.
    /// </summary>
    IReadOnlyDictionary<CkId<CkTypeId>, CkTypeGraph> Types { get; }

    /// <summary>
    ///     Returns the attributes of the model.
    /// </summary>
    IReadOnlyDictionary<CkId<CkAttributeId>, CkAttributeGraph> Attributes { get; }

    /// <summary>
    ///     Returns the association roles of the model.
    /// </summary>
    IReadOnlyDictionary<CkId<CkAssociationRoleId>, CkAssociationRoleGraph> AssociationRoles { get; }

    /// <summary>
    ///     Returns the records of the model.
    /// </summary>
    IReadOnlyDictionary<CkId<CkRecordId>, CkRecordGraph> Records { get; }

    /// <summary>
    ///     Returns the enums of the model.
    /// </summary>
    IReadOnlyDictionary<CkId<CkEnumId>, CkEnumGraph> Enums { get; }

    /// <summary>
    ///     Returns a list of model dependencies.
    /// </summary>
    IReadOnlyDictionary<CkModelId, ICollection<CkModelIdVersionRange>> Dependencies { get; }

    /// <summary>
    ///     Returns a list of model dependencies.
    /// </summary>
    IReadOnlyDictionary<CkModelId, CkModelPropertiesDto> Models { get; }

    /// <summary>
    ///     Returns the root object of the compiled version of a CK model.
    /// </summary>
    /// <returns></returns>
    CkCacheRoot ToCkCacheRoot();

    /// <summary>
    ///     Gets or creates a new attribute.
    /// </summary>
    /// <param name="ckAttributeId"></param>
    /// <param name="ckAttributeDto"></param>
    /// <returns></returns>
    CkAttributeGraph GetOrCreateAttribute(CkId<CkAttributeId> ckAttributeId, CkAttributeDto ckAttributeDto);

    /// <summary>
    ///     Gets or creates a new type.
    /// </summary>
    /// <param name="ckTypeId"></param>
    /// <param name="ckTypeDto"></param>
    /// <returns></returns>
    CkTypeGraph GetOrCreateType(CkId<CkTypeId> ckTypeId, CkCompiledTypeDto ckTypeDto);

    /// <summary>
    ///     Gets or creates a new association role.
    /// </summary>
    /// <param name="ckAssociationId"></param>
    /// <param name="ckAssociationRole"></param>
    /// <returns></returns>
    CkAssociationRoleGraph GetOrCreateAssociationRole(CkId<CkAssociationRoleId> ckAssociationId,
        CkAssociationRoleDto ckAssociationRole);

    /// <summary>
    ///     Gets or creates a new record.
    /// </summary>
    /// <param name="ckRecordId"></param>
    /// <param name="ckRecordDto"></param>
    /// <returns></returns>
    CkRecordGraph GetOrCreateRecord(CkId<CkRecordId> ckRecordId, CkRecordDto ckRecordDto);

    /// <summary>
    ///     Gets or creates a new enum.
    /// </summary>
    /// <param name="ckEnumId"></param>
    /// <param name="ckEnumDto"></param>
    /// <returns></returns>
    CkEnumGraph GetOrCreateEnum(CkId<CkEnumId> ckEnumId, CkEnumDto ckEnumDto);

    /// <summary>
    /// Gets or creates a new model.
    /// </summary>
    /// <param name="ckModelId"></param>
    /// <param name="description"></param>
    /// <returns></returns>
    CkModelPropertiesDto GetOrCreateModel(CkModelId ckModelId, string? description);

    /// <summary>
    ///     Appends the model elements of the given <paramref name="ckCompiledModelRoot" /> to this instance.
    /// </summary>
    /// <param name="ckCompiledModelRoot">The compiled model root to append</param>
    void AppendModel(CkCompiledModelRoot ckCompiledModelRoot);

    /// <summary>
    /// Gets all Types of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    IEnumerable<CkTypeGraph> GetTypes();

    /// <summary>
    /// Gets all Attributes of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    IEnumerable<CkAttributeGraph> GetAttributes();

    /// <summary>
    /// Gets all Enums of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    IEnumerable<CkEnumGraph> GetEnums();

    /// <summary>
    /// Gets all Records of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    IEnumerable<CkRecordGraph> GetRecords();

    /// <summary>
    /// Gets all AssociationRoles of a given ckModelGraph
    /// </summary>
    /// <returns></returns>
    IEnumerable<CkAssociationRoleGraph> GetAssociationRoles();

    /// <summary>
    ///     Returns a list of all query column attribute paths for the given construction kit type
    /// </summary>
    /// <returns></returns>
    IReadOnlyCollection<CkTypeQueryColumn> GetCkTypeQueryColumnPaths(CkId<CkTypeId> ckTypeId, bool ignoreNavigationProperties);
}