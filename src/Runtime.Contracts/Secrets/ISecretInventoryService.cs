using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     Secrets overview of a tenant (AB#5532, handover §7 "Q1"): every Secret slot of every entity -
///     top-level attributes and record members at any depth - with its storage form, key id, "set at" and
///     whether it needs re-entry. Never returns a value or ciphertext. Implemented in Runtime.Engine
///     (<c>SecretInventoryService</c>) over the same scan as the secret sweep and registered by
///     <c>AddRuntimeEngine()</c>; the asset repository exposes it as GraphQL <c>secrets { inventory summary }</c>.
/// </summary>
/// <remarks>
///     Both calls scan the tenant's entities with Secret slots; archived (deleted, <c>RtState.Archived</c>)
///     entities are excluded exactly like in the public queries (AB#5532/AB#5544). Results are live, not
///     cached. Works without keys: without a key ring every protected value is reported as
///     <see cref="SecretStorageForm.KeyMissing" /> (the key id is not in this host's - empty - ring).
/// </remarks>
public interface ISecretInventoryService
{
    /// <summary>
    ///     Lists Secret slots, filtered and paged. Order: CK type (full name), rtId, attribute order of the
    ///     type, record elements in stored order.
    /// </summary>
    /// <param name="tenantId">Tenant</param>
    /// <param name="query">Filters and paging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The requested page and the total number of matching slots</returns>
    Task<SecretInventoryPage> ListAsync(string tenantId, SecretInventoryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Counts all Secret slots of the tenant per storage form.
    /// </summary>
    /// <param name="tenantId">Tenant</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The counts</returns>
    Task<SecretInventorySummary> SummarizeAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
///     Filters and paging of <see cref="ISecretInventoryService.ListAsync" />.
/// </summary>
public sealed class SecretInventoryQuery
{
    /// <summary>
    ///     Only entities of this CK type or a type derived from it (runtime id, e.g.
    ///     <c>System.Communication/SftpConfiguration</c>; a version suffix is accepted). An unknown type yields an
    ///     empty page. <c>null</c> = all types.
    /// </summary>
    public string? CkTypeId { get; init; }

    /// <summary>
    ///     Only slots in one of these storage forms; <c>null</c> or empty = all forms.
    /// </summary>
    public IReadOnlyCollection<SecretStorageForm>? Forms { get; init; }

    /// <summary>
    ///     <c>true</c> = only re-entry tasks, <c>false</c> = only slots that need none, <c>null</c> = all.
    /// </summary>
    public bool? NeedsReEntry { get; init; }

    /// <summary>
    ///     Case-insensitive substring of the rtId, the CK type id (full id such as
    ///     <c>System.Communication/Application</c> or the short type name after the slash), the well-known name,
    ///     the display name or the attribute path (never matched against values). <c>null</c> or empty = no text filter.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    ///     Number of matching slots to skip. Default 0.
    /// </summary>
    public int Skip { get; init; }

    /// <summary>
    ///     Maximum number of slots to return. Default 50; values below 1 return no items (only the total).
    /// </summary>
    public int Take { get; init; } = 50;
}

/// <summary>
///     One Secret slot of an entity. Never carries a value.
/// </summary>
/// <param name="CkTypeId">CK type of the entity (runtime id)</param>
/// <param name="RtId">Runtime id of the entity</param>
/// <param name="RtWellKnownName">Well-known name of the entity, if any</param>
/// <param name="DisplayName">
///     Display name of the entity: the stored display name, else a string <c>Name</c> attribute of the type,
///     else the well-known name; <c>null</c> otherwise (show the rtId)
/// </param>
/// <param name="AttributePath">
///     camelCase path of the slot: <c>password</c>; record members <c>endpoints[key=prod].token</c> (record array,
///     element addressed by its record key; <c>endpoints[0].token</c> when the record declares no key) and
///     <c>credentials.token</c> (single record)
/// </param>
/// <param name="AttributeName">CK attribute name (PascalCase) of the top-level attribute</param>
/// <param name="Required">True when the slot's attribute is required (for a record member: within its record)</param>
/// <param name="Form">Storage form</param>
/// <param name="KeyId">Key id (<see cref="SecretStorageForm.EncV2" /> / <see cref="SecretStorageForm.KeyMissing" /> only)</param>
/// <param name="SetAt">"Set at" (UTC) of a protected value; <c>null</c> for legacy / not set / unknown</param>
/// <param name="NeedsReEntry">
///     <see cref="SecretStorageForm.KeyMissing" /> or <see cref="SecretStorageForm.Corrupt" />, or
///     <see cref="SecretStorageForm.NotSet" /> and <paramref name="Required" />
/// </param>
public sealed record SecretInventoryItem(
    string CkTypeId,
    OctoObjectId RtId,
    string? RtWellKnownName,
    string? DisplayName,
    string AttributePath,
    string AttributeName,
    bool Required,
    SecretStorageForm Form,
    string? KeyId,
    DateTime? SetAt,
    bool NeedsReEntry)
{
    /// <summary>
    ///     The re-entry rule of <see cref="NeedsReEntry" />.
    /// </summary>
    public static bool IsReEntryNeeded(SecretStorageForm form, bool required)
    {
        return form is SecretStorageForm.KeyMissing or SecretStorageForm.Corrupt ||
               (form == SecretStorageForm.NotSet && required);
    }
}

/// <summary>
///     A page of <see cref="ISecretInventoryService.ListAsync" />.
/// </summary>
/// <param name="Items">The slots of the page</param>
/// <param name="TotalCount">Number of slots matching the filters (all pages)</param>
public sealed record SecretInventoryPage(IReadOnlyList<SecretInventoryItem> Items, int TotalCount);

/// <summary>
///     Counts of all Secret slots of a tenant per storage form (<see cref="ISecretInventoryService.SummarizeAsync" />).
/// </summary>
public sealed class SecretInventorySummary
{
    private readonly Dictionary<string, int> _encV2ByKeyId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All slots.</summary>
    public int Total { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.NotSet" />.</summary>
    public int NotSet { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.Plaintext" />.</summary>
    public int Plaintext { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.EncV1" />.</summary>
    public int EncV1 { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.EncV2" />.</summary>
    public int EncV2 { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.KeyMissing" />.</summary>
    public int KeyMissing { get; private set; }

    /// <summary>Slots in <see cref="SecretStorageForm.Corrupt" />.</summary>
    public int Corrupt { get; private set; }

    /// <summary>Slots that need re-entry (<see cref="SecretInventoryItem.NeedsReEntry" />).</summary>
    public int NeedsReEntry { get; private set; }

    /// <summary><see cref="SecretStorageForm.EncV2" /> slots per key id.</summary>
    public IReadOnlyDictionary<string, int> EncV2ByKeyId => _encV2ByKeyId;

    /// <summary>
    ///     Counts one slot.
    /// </summary>
    public void Add(SecretInventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Total++;
        switch (item.Form)
        {
            case SecretStorageForm.NotSet:
                NotSet++;
                break;
            case SecretStorageForm.Plaintext:
                Plaintext++;
                break;
            case SecretStorageForm.EncV1:
                EncV1++;
                break;
            case SecretStorageForm.EncV2:
                EncV2++;
                var key = item.KeyId ?? string.Empty;
                _encV2ByKeyId[key] = _encV2ByKeyId.TryGetValue(key, out var count) ? count + 1 : 1;
                break;
            case SecretStorageForm.KeyMissing:
                KeyMissing++;
                break;
            case SecretStorageForm.Corrupt:
                Corrupt++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item), item.Form, null);
        }

        if (item.NeedsReEntry)
        {
            NeedsReEntry++;
        }
    }
}
