using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Engine.Secrets;

/// <summary>
///     Kind of write a <see cref="ISecretWriteNormalizer" /> prepares.
/// </summary>
public enum SecretWriteOperation
{
    /// <summary>
    ///     A new entity: <c>""</c> and omitted secrets stay unset.
    /// </summary>
    Insert = 0,

    /// <summary>
    ///     A partial update: only attributes present in the entity are written; <c>""</c> is removed from
    ///     the write so the stored value stays unchanged.
    /// </summary>
    Update = 1,

    /// <summary>
    ///     A full replace: an omitted or <c>""</c> secret is carried over from the stored entity.
    /// </summary>
    Replace = 2
}

/// <summary>
///     Where the values handed to a <see cref="ISecretWriteNormalizer" /> come from. Decides what a
///     plain <see cref="string" /> in a Secret slot means.
/// </summary>
public enum SecretValueOrigin
{
    /// <summary>
    ///     API / caller input: a string is a plaintext (<see cref="RtSecretValue.Pending" />) and is always
    ///     encrypted as it is - even when it looks like an envelope, so ciphertext cannot be copied onto
    ///     another entity.
    /// </summary>
    Input = 0,

    /// <summary>
    ///     Values read from storage (CK migrations, sweep): a string is a legacy value
    ///     (<see cref="RtSecretValue.LegacyPlaintext" />) - clear text or <c>enc:v1</c>, which is decrypted
    ///     before it is re-encrypted.
    /// </summary>
    Storage = 1
}

/// <summary>
///     Outcome of <see cref="ISecretWriteNormalizer.Normalize" />.
/// </summary>
public sealed class SecretWriteResult
{
    private readonly List<string> _missingRequired = [];

    /// <summary>
    ///     Paths of required Secret attributes (top-level names, or record paths such as
    ///     <c>Credentials[Key=api].Value</c>) that have no value after the write rules ran - e.g. a
    ///     replace without a value and without a stored value to carry over. Callers reject the write.
    /// </summary>
    public IReadOnlyList<string> MissingRequiredAttributes => _missingRequired;

    /// <summary>
    ///     Number of values encrypted (or re-encrypted) by the call.
    /// </summary>
    public int ProtectedCount { get; internal set; }

    /// <summary>
    ///     Number of values carried over from the stored entity.
    /// </summary>
    public int CarriedOverCount { get; internal set; }

    internal void AddMissing(string path)
    {
        _missingRequired.Add(path);
    }
}

/// <summary>
///     The Secret write step (AB#5532, concept §3.6, §4.6): applied unconditionally by
///     <c>BulkRtMutation</c> (insert, replace, update) and explicitly by the paths that bypass it
///     (<c>RuntimeRepositoryBase.BulkInsertRtEntitiesAsync</c>, the CK migration writes through
///     <c>InsertOneRtEntityForMigrationAsync</c> / <c>RewriteAttributeValueForMigrationAsync</c>).
///     After it ran, a Secret slot holds only <c>null</c>, an <see cref="RtSecretValue" /> in state
///     <see cref="RtSecretValueState.Protected" />, or - on a host without keys, for a value that was
///     already stored that way - <see cref="RtSecretValueState.LegacyPlaintext" />. Never a
///     <see cref="RtSecretValueState.Pending" /> value and never a plain string.
/// </summary>
/// <remarks>
///     <para>Rules for a top-level Secret attribute:</para>
///     <list type="table">
///         <listheader><term>Incoming</term><description>Result</description></listheader>
///         <item><term>non-empty string / <c>Pending</c></term><description>encrypted with the active key; no key configured throws <c>SecretEncryptionNotConfiguredException</c></description></item>
///         <item><term><c>""</c></term><description>removed from the write (update: stored value unchanged; replace: carried over from the stored entity; insert: not set)</description></item>
///         <item><term>omitted</term><description>insert/update: nothing; replace: carried over from the stored entity</description></item>
///         <item><term><c>Protected</c></term><description>passed through (trusted internal callers: upsert preservation, restore, sweep)</description></item>
///         <item><term><c>LegacyPlaintext</c></term><description>re-encrypted with the active key (an <c>enc:v1</c> value is decrypted first); without a key it is kept as it is - it was already stored that way</description></item>
///         <item><term><c>null</c></term><description>cleared</description></item>
///         <item><term>placeholder (<c>&lt;...&gt;</c>, <c>TODO_SET_...</c>)</term><description>stored as <c>null</c> ("not set")</description></item>
///     </list>
///     <para>
///         Secret sub-attributes of records (any nesting depth): a non-empty value or placeholder follows
///         the rules above; <c>null</c>, <c>""</c> or an omitted sub-value is carried over from the stored
///         element with the same record key (<c>CkRecordGraph.RecordKey</c>) for record arrays, from the
///         stored record (by position) for a single record. Without a matching stored element the
///         sub-value is not set. A placeholder is therefore the way to clear a secret inside a record.
///     </para>
/// </remarks>
public interface ISecretWriteNormalizer
{
    /// <summary>
    ///     True when the type or record has a Secret attribute, directly or inside a (nested) record
    ///     attribute. Cached per graph instance; cheap enough for every write.
    /// </summary>
    bool HasSecretAttributes(ICkCacheService ckCacheService, string tenantId, CkTypeWithAttributesGraph graph);

    /// <summary>
    ///     True when the attribute is a Secret attribute, or a <c>Record</c> / <c>RecordArray</c> attribute
    ///     whose record (or a record derived from it, or a nested record) has a Secret attribute.
    /// </summary>
    bool AttributeHasSecrets(ICkCacheService ckCacheService, string tenantId, CkTypeAttributeGraph attribute);

    /// <summary>
    ///     True when <see cref="Normalize" /> needs the stored entity for this write: every replace of a
    ///     type with Secret slots, and an update that writes a record attribute containing secrets.
    /// </summary>
    bool NeedsStoredEntity(ICkCacheService ckCacheService, string tenantId, CkTypeWithAttributesGraph graph,
        RtTypeWithAttributes incoming, SecretWriteOperation operation);

    /// <summary>
    ///     Applies the write rules to <paramref name="incoming" /> in place.
    /// </summary>
    /// <param name="ckCacheService">CK cache to resolve record definitions</param>
    /// <param name="tenantId">Tenant of the cache</param>
    /// <param name="graph">CK type (or record) of <paramref name="incoming" /></param>
    /// <param name="incoming">The entity about to be written; modified in place</param>
    /// <param name="operation">Kind of write</param>
    /// <param name="stored">The stored entity (replace, record carry-over); <c>null</c> when there is none</param>
    /// <param name="origin">What a plain string means</param>
    /// <returns>Required secrets that ended up without a value, and counts</returns>
    /// <exception cref="Meshmakers.Octo.Runtime.Contracts.Secrets.SecretEncryptionNotConfiguredException">
    ///     A value must be encrypted but no active key is configured
    /// </exception>
    SecretWriteResult Normalize(ICkCacheService ckCacheService, string tenantId, CkTypeWithAttributesGraph graph,
        RtTypeWithAttributes incoming, SecretWriteOperation operation, RtTypeWithAttributes? stored = null,
        SecretValueOrigin origin = SecretValueOrigin.Input);

    /// <summary>
    ///     Applies the write rules to a single attribute value for a write that sets one slot (CK
    ///     migration rewrite): a Secret value or a record (array) value containing secrets. <c>""</c> and
    ///     placeholders become <c>null</c> (a single-slot write cannot "leave unchanged"); there is no
    ///     carry-over. Values of other attribute types are returned unchanged.
    /// </summary>
    object? NormalizeAttributeValue(ICkCacheService ckCacheService, string tenantId, CkTypeAttributeGraph attribute,
        object? value, SecretValueOrigin origin);
}
