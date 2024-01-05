namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception during cache operations
/// </summary>
public class CkCacheException : CkModelException
{
    /// <inheritdoc />
    public CkCacheException()
    {
    }

    /// <inheritdoc />
    public CkCacheException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public CkCacheException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception CkCacheNotFound(string tenantId)
    {
        return new CkCacheException($"CkCache for tenant '{tenantId}' not found.");
    }

    internal static Exception CacheUnloaded(string tenantId)
    {
        return new CkCacheException($"CkCache for tenant '{tenantId}' not loaded.");
    }

    internal static Exception CkTypeIdNotFound(string tenantId, CkId<CkTypeId> ckTypeId)
    {
        return new CkCacheException($"CkTypeId '{ckTypeId}' not found in CkCache for tenant '{tenantId}'.");
    }

    internal static Exception CkAttributeIdNotFound(string tenantId, CkId<CkAttributeId> ckAttributeId)
    {
        return new CkCacheException($"CkAttributeId '{ckAttributeId}' not found in CkCache for tenant '{tenantId}'.");
    }

    internal static Exception CkAssociationRoleNotFound(string tenantId, CkId<CkAssociationRoleId> ckAssociationRoleId)
    {
        return new CkCacheException($"CkAssociationRole '{ckAssociationRoleId}' not found in CkCache for tenant '{tenantId}'.");
    }

    internal static Exception CkRecordNotFound(string tenantId, CkId<CkRecordId> ckRecordId)
    {
        return new CkCacheException($"CkRecordId '{ckRecordId}' not found in CkCache for tenant '{tenantId}'.");
    }

    internal static Exception CannotDeserializeCache(string tenantId)
    {
        return new CkCacheException($"Cannot deserialize CkCache for tenant '{tenantId}'.");
    }

    internal static Exception CkEnumNotFound(string tenantId, CkId<CkEnumId> ckEnumId)
    {
        return new CkCacheException($"CkEnumId '{ckEnumId}' not found in CkCache for tenant '{tenantId}'.");
    }
}