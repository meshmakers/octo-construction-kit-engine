using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Server-side access to the plaintext of a Secret attribute (concept §3.7). Every call is a
///     counted decrypt; keep callers on the architecture-test allowlist.
/// </summary>
public static class SecretAttributeExtensions
{
    /// <summary>
    ///     Returns the plaintext of the Secret attribute <paramref name="attributeName" />, or
    ///     <c>null</c> when it is not set. A stored value that cannot be read (unknown key id, corrupt) is
    ///     treated as not set and returns <c>null</c> as well; the protector logs an error without the value
    ///     (<see cref="ISecretAttributeProtector.RevealOrNull" />, decisions 2026-10-06 item 2).
    /// </summary>
    /// <param name="entity">Entity or record holding the attribute</param>
    /// <param name="attributeName">Attribute name in PascalCase</param>
    /// <param name="protector">The protector</param>
    /// <param name="tenantId">Tenant id for the decrypt counter (optional)</param>
    /// <returns>The plaintext or <c>null</c></returns>
    public static string? GetSecretPlaintext(this RtTypeWithAttributes entity, string attributeName,
        ISecretAttributeProtector protector, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(protector);

        var value = entity.GetAttributeSecretValueOrDefault(attributeName);
        if (value == null)
        {
            return null;
        }

        var ckTypeId = entity is RtEntity rtEntity ? rtEntity.CkTypeId?.ToString() : null;
        return protector.RevealOrNull(value, new SecretAccessContext(tenantId, ckTypeId, attributeName));
    }

    /// <summary>
    ///     Returns the read state of the Secret attribute <paramref name="attributeName" /> without decrypting
    ///     it (<see cref="ISecretAttributeProtector.GetReadState" />).
    /// </summary>
    /// <param name="entity">Entity or record holding the attribute</param>
    /// <param name="attributeName">Attribute name in PascalCase</param>
    /// <param name="protector">The protector</param>
    /// <param name="tenantId">Tenant id for log and counter tags (optional)</param>
    /// <returns>The read state</returns>
    public static SecretValueState GetSecretReadState(this RtTypeWithAttributes entity, string attributeName,
        ISecretAttributeProtector protector, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(protector);

        var value = entity.GetAttributeSecretValueOrDefault(attributeName);
        var ckTypeId = entity is RtEntity rtEntity ? rtEntity.CkTypeId?.ToString() : null;
        return protector.GetReadState(value, new SecretAccessContext(tenantId, ckTypeId, attributeName));
    }
}
