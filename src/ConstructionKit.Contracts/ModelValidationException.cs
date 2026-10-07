
using System.Globalization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during model validation operations.
/// </summary>
public class ModelValidationException : CkModelException
{
    /// <inheritdoc />
    public ModelValidationException()
    {
    }

    /// <inheritdoc />
    public ModelValidationException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public ModelValidationException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception UnknownCkTypeIdForInheritance(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{ckTypeId}' is used as base type but is an unknown CkTypeId. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception UnknownCkTypeId(CkId<CkTypeId> ckTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{ckTypeId}' is unknown. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception UnknownCkRecordId(CkId<CkRecordId> ckRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{ckRecordId}' is unknown. This may happen because a dependency to another construction kit model is missing.");
    }


    internal static Exception UnknownCkTypeIdForAssociationTarget(CkId<CkTypeId> originCkTypeId,
        CkId<CkAssociationRoleId> entityAssociationRoleId, CkId<CkTypeId> typeAssociationTargetCkTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{originCkTypeId}' defines a unknown target construction kit type id '{typeAssociationTargetCkTypeId}' for role id '{entityAssociationRoleId}'." +
            $" This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception DerivedFromCkTypeIdThatIsFinal(CkId<CkTypeId> currentCkTypeId, CkId<CkTypeId> lastCkTypeId)
    {
        return new ModelValidationException(
            $"CkTypeId '{currentCkTypeId}' is final, but CkTypeId '{lastCkTypeId}' is derived from it.");
    }

    internal static Exception InheritanceMissing(string typeId)
    {
        return new ModelValidationException($"Name '{typeId}' has no inheritance definition. Ensure that attribute ckDerivedId is set.");
    }

    internal static Exception ModelIdContainsInvalidCharacters(string modelId)
    {
        return new ModelValidationException($"Name '{modelId}' contains invalid characters. Only a-z, A-Z, 0-9, _ and . are allowed.");
    }

    internal static Exception DerivedFromCkRecordIdThatIsFinal(CkId<CkRecordId> currentCkRecordId, CkId<CkRecordId> lastCkRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{currentCkRecordId}' is final, but CkRecordId '{lastCkRecordId}' is derived from it.");
    }

    internal static Exception UnknownCkRecordIdForInheritance(CkId<CkRecordId> ckRecordId)
    {
        return new ModelValidationException(
            $"CkRecordId '{ckRecordId}' is used as base record type but is an unknown CkRecordId. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception DuplicateAttributeNamesInCkRecord(CkId<CkRecordId> ckRecordId, IEnumerable<string> select)
    {
        var attributeNames = string.Join(", ", select);
        return new ModelValidationException($"CkRecordId '{ckRecordId}' has duplicate attribute names: '{attributeNames}'");
    }

    internal static Exception DuplicateAttributeIdsInCkRecord(CkId<CkRecordId> ckRecordId,
        IEnumerable<CkId<CkAttributeId>> duplicateAttributeIds)
    {
        var attributeIds = string.Join(", ", duplicateAttributeIds);
        return new ModelValidationException($"CkRecordId '{ckRecordId}' has duplicate attribute IDs: '{attributeIds}'");
    }

    internal static Exception UnknownCkModel(CkModelIdVersionRange ckDependency)
    {
        return new ModelValidationException(
            $"Dependency '{ckDependency}' is an unknown construction kit model library. This may happen because a dependency to another construction kit model is missing.");
    }

    internal static Exception UnknownCkModels(IReadOnlyCollection<CkModelIdVersionRange> resolveResultUnresolvedDependencyModelIds)
    {
        var modelIds = string.Join(", ",
            resolveResultUnresolvedDependencyModelIds.Select(d => d.ToString(CultureInfo.InvariantCulture)));
        return new ModelValidationException(
            $"Dependencies '{modelIds}' are unknown construction kit model libraries. This may happen because dependencies to other construction kit models are missing.");
    }

    /// <summary>
    ///     AB#5661: unknown dependencies with the versions the readable catalogs do know, so a stale or
    ///     out-of-order build is diagnosable from the message alone.
    /// </summary>
    internal static Exception UnknownCkModels(IReadOnlyCollection<CkModelIdVersionRange> unresolvedDependencies,
        IReadOnlyDictionary<string, string> visibleVersionsByModel)
    {
        var modelIds = string.Join(", ",
            unresolvedDependencies.Select(d => d.ToString(CultureInfo.InvariantCulture)));
        var details = string.Join("; ", unresolvedDependencies.Select(d =>
            visibleVersionsByModel.TryGetValue(d.Name, out var versions) && !string.IsNullOrEmpty(versions)
                ? $"'{d}' does not match any visible version of {d.Name} ({versions})"
                : $"no catalog knows any version of {d.Name}"));
        return new ModelValidationException(
            $"Dependencies '{modelIds}' are unknown construction kit model libraries: {details}. " +
            "If the dependency is built in the same repository or build, the dependent project must reference it " +
            "(ProjectReference) so it is compiled and published to the LocalFileSystemCatalog first; otherwise " +
            "publish the dependency, or check that the dependency range is correct.");
    }

    /// <summary>
    ///     AB#5664: a range-retaining model references elements of a dependency that do not exist at the floor
    ///     of the declared range.
    /// </summary>
    internal static Exception ReferenceMissingAtFloor(CkModelId modelId, CkModelIdVersionRange range,
        CkModelId floorModelId, CkModelId resolvedModelId, IEnumerable<string> missingElements)
    {
        return new ModelValidationException(
            $"Model '{modelId}' references elements that do not exist at the floor of its dependency range " +
            $"'{range}': {string.Join(", ", missingElements)} (present in '{resolvedModelId}', missing in " +
            $"'{floorModelId}'). A tenant with '{floorModelId}' installed could not resolve the model. Raise the " +
            "lower bound of the dependency range to the first version that has these elements.");
    }

    internal static Exception MultipleVersionsOfCkModel(string modelName, IEnumerable<CkModelId> conflictingModelIds, IEnumerable<CkModelId> originModelIds)
    {
        var versions = string.Join(", ", conflictingModelIds.Select(m => m.FullName));
        var origins = string.Join(", ", originModelIds.Select(m => m.FullName));
        return new ModelValidationException(
            $"Multiple versions of construction kit model '{modelName}' were resolved as transitive dependencies: {versions}. " +
            $"Conflicting versions are referenced by: {origins}. " +
            "This typically happens when different catalogs (LocalFileSystem, public/private GitHub) hold dependents that pin different versions of the same model. " +
            "Resolutions: rebuild the conflicting dependents against a single common version, narrow the dependency range in the consumer's ckModel.yaml, " +
            "or disable catalogs that hold stale entries (MSBuild properties OctoPublicGitHubCatalogIsEnabled / OctoPrivateGitHubCatalogIsEnabled).");
    }
}