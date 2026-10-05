using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Maintenance of the stored values of <c>Secret</c> attributes of a tenant (AB#5532, concept §5.2
///     phase 4, §5.3, §6): verify, encrypt legacy values, re-protect with the active key, clear values
///     of unknown key ids after a cross-environment restore, and the emergency decrypt. Implemented in
///     Runtime.Engine (<c>SecretMaintenanceService</c>) and registered by <c>AddRuntimeEngine()</c>.
///     The bot <c>SecretSweepJob</c> (WP9), the system API and the octo-cli commands
///     (<c>SecretStatus</c>, <c>ReprotectSecrets</c>) build on it.
/// </summary>
/// <remarks>
///     <para>
///         The service walks every concrete CK type of the tenant that has a Secret attribute (top-level
///         or inside a record, at any nesting depth), reads the entities in batches through
///         <c>IRuntimeRepository.GetRtEntitiesByTypeAsync</c> (archived entities included) and rewrites
///         changed attributes through <c>IRuntimeRepository.RewriteAttributeValueIfUnchangedForMigrationAsync</c>
///         (a record-valued attribute is rewritten as a whole). The rewrite is conditional on the stored
///         value still being the one the sweep read; an attribute changed in between is skipped and
///         counted in <c>SecretSweepResult.SkippedConcurrentlyModified</c>. It works on the values as the
///         repository returns them: <c>RtSecretValue.Protected</c> for an <c>enc:v2</c> sub-document,
///         <c>RtSecretValue.LegacyPlaintext</c> or a plain string for a legacy string slot.
///     </para>
///     <para>
///         Results carry counts and names only - never a value, never ciphertext. Every mode is
///         idempotent; a second run of the same mode rewrites nothing.
///     </para>
/// </remarks>
public interface ISecretMaintenanceService
{
    /// <summary>
    ///     Runs a sweep over all Secret values of a tenant with the default options.
    ///     <see cref="SecretSweepMode.Decrypt" /> is refused here; it needs
    ///     <see cref="SecretSweepOptions.ConfirmDecrypt" /> through the other overload.
    /// </summary>
    /// <param name="tenantId">Tenant to sweep</param>
    /// <param name="mode">What to do with the values found</param>
    /// <param name="cancellationToken">Cancellation token; a cancelled sweep keeps what it already rewrote</param>
    /// <returns>Counts per form and per CK type / attribute, cleared values and failures</returns>
    Task<SecretSweepResult> SweepTenantAsync(string tenantId, SecretSweepMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs a sweep over all Secret values of a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant to sweep</param>
    /// <param name="mode">What to do with the values found</param>
    /// <param name="options">Batch size, decrypt confirmation, CK model filter</param>
    /// <param name="cancellationToken">Cancellation token; a cancelled sweep keeps what it already rewrote</param>
    /// <returns>Counts per form and per CK type / attribute, cleared values and failures</returns>
    /// <exception cref="InvalidOperationException">
    ///     <see cref="SecretSweepMode.Decrypt" /> without <see cref="SecretSweepOptions.ConfirmDecrypt" />
    /// </exception>
    /// <exception cref="SecretEncryptionNotConfiguredException">
    ///     A mode that writes or decrypts (everything but <see cref="SecretSweepMode.Verify" />) on a host
    ///     without an active key
    /// </exception>
    Task<SecretSweepResult> SweepTenantAsync(string tenantId, SecretSweepMode mode, SecretSweepOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     CK migration hook (concept §5.2 phase 3): when a model switches attributes from <c>String</c> to
    ///     <c>Secret</c>, stored placeholders (<c>&lt;...&gt;</c>, <c>TODO_SET_...</c>) and empty strings in
    ///     Secret slots become <c>null</c> ("not set"). Nothing is encrypted and no key is needed - the
    ///     encrypt sweep does that later. Called by the CK model migration service after a successful
    ///     migration of <paramref name="ckModelName" />.
    /// </summary>
    /// <param name="tenantId">Tenant</param>
    /// <param name="ckModelName">
    ///     Only CK types of this model, or types whose Secret attributes are defined by it, are scanned;
    ///     <c>null</c> = every type with a Secret attribute
    /// </param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The scan result; <see cref="SecretSweepResult.PlaceholdersNormalized" /> counts the normalised slots</returns>
    Task<SecretSweepResult> NormalizePlaceholdersAsync(string tenantId, string? ckModelName,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Mode of a secret sweep (concept §5.2).
/// </summary>
public enum SecretSweepMode
{
    /// <summary>
    ///     Count only - nothing is decrypted or written. Recurring after the encrypt sweep; strict mode
    ///     starts 14 days after it reports zero plaintext (decision 10).
    /// </summary>
    Verify = 0,

    /// <summary>
    ///     Legacy values (clear text and <c>enc:v1</c>) become <c>enc:v2</c> with the active key;
    ///     placeholders and empty strings become <c>null</c>. <c>enc:v2</c> values stay as they are,
    ///     whatever their key id.
    /// </summary>
    Encrypt = 1,

    /// <summary>
    ///     Everything not protected with the active key (legacy and <c>enc:v2</c> of another known key id)
    ///     is re-encrypted with the active key - key rotation. Values with an unknown key id cannot be
    ///     decrypted and are only counted (use <see cref="ClearUnknownKid" />).
    /// </summary>
    Reprotect = 2,

    /// <summary>
    ///     <c>enc:v2</c> values whose key id is not in the key ring become <c>null</c> ("not set") and are
    ///     listed in <see cref="SecretSweepResult.Cleared" /> - the re-entry report after a
    ///     cross-environment or child-tenant restore (decision 5).
    /// </summary>
    ClearUnknownKid = 3,

    /// <summary>
    ///     EMERGENCY ONLY (rollback after phase 4, concept §5.2): every decryptable value is written back
    ///     as a clear-text string, so binaries without the Secret type can read it again. Requires
    ///     <see cref="SecretSweepOptions.ConfirmDecrypt" />. Treat the tenant as exposed afterwards and run
    ///     <see cref="Encrypt" /> as soon as the emergency is over.
    /// </summary>
    Decrypt = 4
}

/// <summary>
///     Options of a secret sweep.
/// </summary>
public sealed class SecretSweepOptions
{
    /// <summary>
    ///     Number of entities read per repository call. Default 500.
    /// </summary>
    public int BatchSize { get; init; } = 500;

    /// <summary>
    ///     Must be <c>true</c> for <see cref="SecretSweepMode.Decrypt" /> - the explicit confirmation that
    ///     clear text is written back to the database on purpose (emergency rollback only).
    /// </summary>
    public bool ConfirmDecrypt { get; init; }

    /// <summary>
    ///     Restricts the sweep to CK types of this model or types whose Secret attributes are defined by
    ///     it; <c>null</c> = all types.
    /// </summary>
    public string? CkModelName { get; init; }

    /// <summary>
    ///     Default options.
    /// </summary>
    public static SecretSweepOptions Default => new();
}

/// <summary>
///     Stored form of a Secret value as the sweep classifies it.
/// </summary>
public enum SecretValueForm
{
    /// <summary>
    ///     No value (<c>null</c> or missing).
    /// </summary>
    NotSet = 0,

    /// <summary>
    ///     A legacy string slot holding a placeholder (<c>&lt;...&gt;</c>, <c>TODO_SET_...</c>) or <c>""</c> -
    ///     semantically "not set", normalised to <c>null</c> by <see cref="SecretSweepMode.Encrypt" />.
    /// </summary>
    Placeholder = 1,

    /// <summary>
    ///     A legacy string slot holding clear text.
    /// </summary>
    Plaintext = 2,

    /// <summary>
    ///     A legacy string slot holding an <c>enc:v1:</c> envelope (octo-sdk <c>InstanceSecretCrypto</c>).
    /// </summary>
    EncV1 = 3,

    /// <summary>
    ///     An <c>enc:v2:&lt;kid&gt;:</c> envelope whose key id is in the key ring.
    /// </summary>
    EncV2 = 4,

    /// <summary>
    ///     An <c>enc:v2:&lt;kid&gt;:</c> envelope whose key id is NOT in the key ring.
    /// </summary>
    UnknownKeyId = 5
}

/// <summary>
///     Counts of Secret values per stored form. Mutable while a sweep runs, read-only for consumers.
/// </summary>
public sealed class SecretFormCounts
{
    private readonly Dictionary<string, long> _encV2ByKeyId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _unknownByKeyId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Slots without a value.
    /// </summary>
    public long NotSet { get; private set; }

    /// <summary>
    ///     Legacy string slots with a placeholder or <c>""</c>.
    /// </summary>
    public long Placeholder { get; private set; }

    /// <summary>
    ///     Legacy string slots with clear text.
    /// </summary>
    public long Plaintext { get; private set; }

    /// <summary>
    ///     Legacy string slots with an <c>enc:v1</c> envelope.
    /// </summary>
    public long EncV1 { get; private set; }

    /// <summary>
    ///     <c>enc:v2</c> values with a known key id (all key ids together).
    /// </summary>
    public long EncV2 { get; private set; }

    /// <summary>
    ///     <c>enc:v2</c> values with a known key id, per key id.
    /// </summary>
    public IReadOnlyDictionary<string, long> EncV2ByKeyId => _encV2ByKeyId;

    /// <summary>
    ///     <c>enc:v2</c> values whose key id is not in the key ring.
    /// </summary>
    public long UnknownKeyId { get; private set; }

    /// <summary>
    ///     <c>enc:v2</c> values whose key id is not in the key ring, per key id.
    /// </summary>
    public IReadOnlyDictionary<string, long> UnknownKeyIdByKeyId => _unknownByKeyId;

    /// <summary>
    ///     Values the sweep could not process (decrypt failed, rewrite failed, unexpected value type).
    ///     Counted in addition to their form.
    /// </summary>
    public long Failed { get; private set; }

    /// <summary>
    ///     All classified slots (failures are counted in their form as well and not added again).
    /// </summary>
    public long Total => NotSet + Placeholder + Plaintext + EncV1 + EncV2 + UnknownKeyId;

    /// <summary>
    ///     Counts one value of the given form.
    /// </summary>
    /// <param name="form">The form</param>
    /// <param name="keyId">Key id for <see cref="SecretValueForm.EncV2" /> and <see cref="SecretValueForm.UnknownKeyId" /></param>
    public void Add(SecretValueForm form, string? keyId = null)
    {
        switch (form)
        {
            case SecretValueForm.NotSet:
                NotSet++;
                break;
            case SecretValueForm.Placeholder:
                Placeholder++;
                break;
            case SecretValueForm.Plaintext:
                Plaintext++;
                break;
            case SecretValueForm.EncV1:
                EncV1++;
                break;
            case SecretValueForm.EncV2:
                EncV2++;
                Increment(_encV2ByKeyId, keyId);
                break;
            case SecretValueForm.UnknownKeyId:
                UnknownKeyId++;
                Increment(_unknownByKeyId, keyId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(form), form, null);
        }
    }

    /// <summary>
    ///     Counts one failed value.
    /// </summary>
    public void AddFailure()
    {
        Failed++;
    }

    private static void Increment(Dictionary<string, long> counts, string? keyId)
    {
        var key = keyId ?? string.Empty;
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
    }
}

/// <summary>
///     Counts for one Secret slot of one CK type.
/// </summary>
/// <param name="CkTypeId">CK type (runtime id, e.g. <c>System.Communication/SftpConfiguration</c>)</param>
/// <param name="AttributePath">
///     Attribute name, or the path into records: <c>Overrides[].Value</c> for a record array,
///     <c>Primary.Value</c> for a single record
/// </param>
/// <param name="Counts">Counts per form</param>
public sealed record SecretSlotReport(string CkTypeId, string AttributePath, SecretFormCounts Counts);

/// <summary>
///     A Secret value the sweep set to <c>null</c> - the re-entry report (decision 5).
/// </summary>
/// <param name="CkTypeId">CK type of the entity</param>
/// <param name="RtId">Runtime id of the entity</param>
/// <param name="AttributePath">
///     Attribute name or record path; a record array element is addressed by its record key, e.g.
///     <c>Overrides[Key=apiToken].Value</c>, or by index when the record declares no key
/// </param>
/// <param name="PreviousForm">The form the value had</param>
/// <param name="KeyId">The key id of the envelope (unknown key id) or <c>null</c></param>
public sealed record SecretSweepClearedValue(
    string CkTypeId,
    OctoObjectId RtId,
    string AttributePath,
    SecretValueForm PreviousForm,
    string? KeyId);

/// <summary>
///     A Secret value the sweep could not process. <see cref="Reason" /> never contains the value.
/// </summary>
/// <param name="CkTypeId">CK type of the entity</param>
/// <param name="RtId">Runtime id of the entity</param>
/// <param name="AttributePath">Attribute name or record path</param>
/// <param name="Reason">Exception type and a value-free description</param>
public sealed record SecretSweepFailure(string CkTypeId, OctoObjectId RtId, string AttributePath, string Reason);

/// <summary>
///     Result of a secret sweep (concept §5.2, §5.3). Counts are the forms as FOUND, before the sweep
///     acted on them; <see cref="ValuesRewritten" /> says how many were changed.
/// </summary>
public sealed class SecretSweepResult
{
    /// <summary>
    ///     Creates a new result.
    /// </summary>
    public SecretSweepResult(string tenantId, SecretSweepMode mode)
    {
        TenantId = tenantId;
        Mode = mode;
        StartedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Tenant that was swept.
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    ///     Mode of the sweep.
    /// </summary>
    public SecretSweepMode Mode { get; }

    /// <summary>
    ///     Start (UTC).
    /// </summary>
    public DateTime StartedAt { get; }

    /// <summary>
    ///     End (UTC); <c>null</c> while running.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    ///     CK types scanned (concrete types with at least one Secret slot).
    /// </summary>
    public int CkTypesScanned { get; set; }

    /// <summary>
    ///     Entities read.
    /// </summary>
    public long EntitiesScanned { get; set; }

    /// <summary>
    ///     Entities with at least one rewritten attribute.
    /// </summary>
    public long EntitiesRewritten { get; set; }

    /// <summary>
    ///     Secret values changed (encrypted, re-protected, cleared, decrypted).
    /// </summary>
    public long ValuesRewritten { get; set; }

    /// <summary>
    ///     Counts per form over the whole tenant.
    /// </summary>
    public SecretFormCounts Totals { get; } = new();

    /// <summary>
    ///     Counts per CK type and Secret slot.
    /// </summary>
    public List<SecretSlotReport> Slots { get; } = [];

    /// <summary>
    ///     Values set to <c>null</c> because they were lost (an <c>enc:v2</c> envelope with an unknown key id,
    ///     <see cref="SecretSweepMode.ClearUnknownKid" />) - the re-entry report (decision 5). Normalised
    ///     placeholders are not listed here; they were never set (see <see cref="PlaceholdersNormalized" />).
    /// </summary>
    public List<SecretSweepClearedValue> Cleared { get; } = [];

    /// <summary>
    ///     Placeholders (<c>&lt;...&gt;</c>, <c>TODO_SET_...</c>) and empty strings in Secret slots that were
    ///     set to <c>null</c> ("not set") by <see cref="SecretSweepMode.Encrypt" />,
    ///     <see cref="SecretSweepMode.Reprotect" /> or <see cref="ISecretMaintenanceService.NormalizePlaceholdersAsync" />.
    ///     Included in <see cref="ValuesRewritten" />.
    /// </summary>
    public long PlaceholdersNormalized { get; set; }

    /// <summary>
    ///     Attributes the sweep would have rewritten but left alone because their stored value changed
    ///     after the sweep read it (conditional rewrite, AB#5532). Not a failure: the newer value was
    ///     written by someone else and the next sweep processes it. Their changes are not counted in
    ///     <see cref="ValuesRewritten" />.
    /// </summary>
    public long SkippedConcurrentlyModified { get; set; }

    /// <summary>
    ///     Values that could not be processed.
    /// </summary>
    public List<SecretSweepFailure> Failures { get; } = [];

    /// <summary>
    ///     True when nothing failed.
    /// </summary>
    public bool Success => Totals.Failed == 0 && Failures.Count == 0;
}
