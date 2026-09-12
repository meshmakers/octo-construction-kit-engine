namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Declares who owns the value of a CK attribute and whether that value is part of the
///     entity's portable definition. Replaces the <c>isRuntimeState</c> boolean, which conflated
///     the two questions (AB#5187).
/// </summary>
/// <remarks>
///     <para>
///         The boolean answered "preserve on upsert?" and "exclude from <c>ExportRt</c>?" with a
///         single bit. That works for a rotating refresh token (preserve, never export) and for a
///         product endpoint (overwrite, export), but not for tenant master data — a tariff, an
///         IBAN, a market-partner id, a logo — which must be preserved AND exported.
///         <see cref="TenantOwned" /> is the value that combination needs.
///     </para>
///     <para>
///         The value on the attribute <em>definition</em> is the default; a type-attribute or
///         record-attribute assignment may override it (<see cref="CkTypeAttributeDto.Ownership" />),
///         so a shared definition like <c>ClientId</c> can be <see cref="Secret" /> on
///         <c>FinApiConfiguration</c> and <see cref="SeedOwned" /> on
///         <c>ServiceAccountConfiguration</c>.
///     </para>
///     <para>
///         Author decision matrix — answer the first question that applies and stop:
///         <list type="number">
///             <item>
///                 Is it a credential, token, key or password — anything you would not paste into
///                 a ticket? → <see cref="Secret" />
///             </item>
///             <item>
///                 Is it written by a service, operator, pipeline or job rather than typed by a
///                 human (status, timestamps, counters, error history, cursors, rotated tokens,
///                 deployed hostname/chart version, debug toggles)? → <see cref="RuntimeState" />
///             </item>
///             <item>
///                 Would a tenant admin set this in the product and be right to be angry if a
///                 product update overwrote it (tariffs, IBAN, market ids, their mailbox/host/port,
///                 logo, colours, app title)? → <see cref="TenantOwned" />
///             </item>
///             <item>
///                 Otherwise it ships with the product and a new version must be able to correct it
///                 (endpoints the product owns, report template names, statutory defaults,
///                 well-known-name wiring, taxonomy). → <see cref="SeedOwned" />
///             </item>
///         </list>
///         Tie-breaker: if you cannot name how a correction would reach every tenant, choose
///         <see cref="SeedOwned" /> — that is the reversible mistake. Marked does not mean frozen:
///         a CK migration <c>Update</c> action is the sanctioned, versioned, auditable way to
///         correct a tenant-owned value across tenants.
///     </para>
/// </remarks>
public enum AttributeOwnershipDto
{
    /// <summary>
    ///     The blueprint / seed author owns the value. A re-apply overwrites the tenant's value and
    ///     the value is carried in an <c>ExportRt</c>. This is the default and it is what an
    ///     attribute without any marker has always done (<c>isRuntimeState: false</c> / absent).
    /// </summary>
    SeedOwned = 0,

    /// <summary>
    ///     The tenant owns the value; the seed may only initialise it. A re-apply keeps the
    ///     tenant's existing value, and the value IS carried in an <c>ExportRt</c> because it is
    ///     part of the entity's portable definition. <c>defaultValues</c> still seed a fresh
    ///     tenant — "seed-initialised, then tenant-owned" is exactly this value.
    ///     Typical: tariffs, IBAN and account holder, market-partner ids, a tenant's mailbox /
    ///     host / port, branding, app title, logos, colours.
    /// </summary>
    TenantOwned = 1,

    /// <summary>
    ///     A service, operator, pipeline or job owns the value at runtime. A re-apply keeps the
    ///     existing value and the value is excluded from an <c>ExportRt</c> — it is instance-local
    ///     live state, not part of the portable definition. Equivalent to the legacy
    ///     <c>isRuntimeState: true</c>.
    ///     Typical: deployment/communication status, last-error pairs, sync cursors, execution and
    ///     statistics history, debug toggles, chart version / hostname written by the operator.
    /// </summary>
    RuntimeState = 2,

    /// <summary>
    ///     A credential. A re-apply keeps the existing value and the value is excluded from an
    ///     <c>ExportRt</c>. Behaves identically to <see cref="RuntimeState" /> today; it exists so
    ///     the model can say what the value actually is — an API key is not "runtime state" — so a
    ///     later export opt-in can re-include <see cref="RuntimeState" /> while never including a
    ///     secret, and so a later redaction / external-vault feature has something to hang off.
    ///     Typical: client secrets, API keys, bot tokens, passwords, private keys, refresh tokens.
    /// </summary>
    Secret = 3
}

/// <summary>
///     Resolution and behaviour predicates for <see cref="AttributeOwnershipDto" />. Every consumer
///     goes through these instead of testing the enum inline, so the two behaviours the ownership
///     model separates stay separated in exactly one place each.
/// </summary>
public static class AttributeOwnership
{
    /// <summary>
    ///     The value an attribute declaration resolves to. <paramref name="ownership" /> wins when
    ///     declared; otherwise the deprecated <c>isRuntimeState</c> alias is mapped
    ///     (<c>true</c> → <see cref="AttributeOwnershipDto.RuntimeState" />, <c>false</c> / absent →
    ///     <see cref="AttributeOwnershipDto.SeedOwned" />). Every declaration that exists today
    ///     therefore resolves with zero behaviour change.
    /// </summary>
    public static AttributeOwnershipDto Resolve(AttributeOwnershipDto? ownership, bool isRuntimeState)
    {
        return ownership ?? (isRuntimeState ? AttributeOwnershipDto.RuntimeState : AttributeOwnershipDto.SeedOwned);
    }

    /// <summary>
    ///     Effective ownership of an attribute assignment: the assignment's override wins, the
    ///     attribute definition's value is the default. <c>null</c> on the assignment means
    ///     "inherit", which is what every assignment that exists today declares.
    /// </summary>
    public static AttributeOwnershipDto Resolve(AttributeOwnershipDto? assignmentOwnership,
        AttributeOwnershipDto definitionOwnership)
    {
        return assignmentOwnership ?? definitionOwnership;
    }

    /// <summary>
    ///     True when an <c>Upsert</c> import must keep the tenant's existing value instead of the
    ///     incoming seed value — i.e. everything except
    ///     <see cref="AttributeOwnershipDto.SeedOwned" />. This is the predicate
    ///     <c>ImportRtModelCommand.PreserveRuntimeStateAttributesAsync</c> filters on, and it is
    ///     what the deprecated <c>isRuntimeState</c> boolean now mirrors.
    /// </summary>
    public static bool IsPreservedOnUpsert(this AttributeOwnershipDto ownership)
    {
        return ownership != AttributeOwnershipDto.SeedOwned;
    }

    /// <summary>
    ///     True when the value must not be carried in an exported runtime model
    ///     (<c>ExportRt</c>) — <see cref="AttributeOwnershipDto.RuntimeState" /> because a
    ///     re-import would overwrite live state with stale values (Bug #1458), and
    ///     <see cref="AttributeOwnershipDto.Secret" /> because a credential is not part of a
    ///     portable model. <see cref="AttributeOwnershipDto.TenantOwned" /> IS exported: that is
    ///     the capability the boolean could not express.
    /// </summary>
    public static bool IsExcludedFromExport(this AttributeOwnershipDto ownership)
    {
        return ownership is AttributeOwnershipDto.RuntimeState or AttributeOwnershipDto.Secret;
    }
}
