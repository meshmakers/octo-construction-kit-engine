using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Represents an entity update info.
/// </summary>
/// <typeparam name="TEntity"></typeparam>
public class EntityUpdateInfo<TEntity> : IEntityUpdateInfo<TEntity>
    where TEntity : RtEntity
{
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    private EntityUpdateInfo(OctoObjectId? rtId, RtCkId<CkTypeId> ckTypeId, TEntity? rtEntity, EntityModOptions modOption,
        IReadOnlyCollection<string>? clearSecretAttributes = null)
    {
        RtId = rtId;
        CkTypeId = ckTypeId;
        ModOption = modOption;
        RtEntity = rtEntity;
        ClearSecretAttributes = NormaliseClearList(clearSecretAttributes);
    }

    private EntityUpdateInfo(RtEntityId rtEntityId, TEntity rtEntity, EntityModOptions modOption)
        : this(rtEntityId, modOption)
    {
        RtEntity = rtEntity;
    }

    private EntityUpdateInfo(RtEntityId rtEntityId, TEntity rtEntity, EntityModOptions modOption,
        AttributeNewerThanGuard? updateGuard, IEnumerable<string>? clearSecretAttributes)
        : this(rtEntityId, modOption)
    {
        RtEntity = rtEntity;
        UpdateGuard = updateGuard;
        ClearSecretAttributes = NormaliseClearList(clearSecretAttributes);
    }

    private EntityUpdateInfo(RtCkId<CkTypeId> ckTypeId, TEntity rtEntity, EntityModOptions modOption)
    {
        CkTypeId = ckTypeId;
        RtEntity = rtEntity;
        ModOption = modOption;
    }

    private EntityUpdateInfo(RtEntityId rtEntityId, EntityModOptions modOption)
    {
        RtId = rtEntityId.RtId;
        CkTypeId = rtEntityId.CkTypeId;
        ModOption = modOption;
    }

    /// <inheritdoc />
    public TEntity? RtEntity { get; }

    /// <inheritdoc />
    public OctoObjectId? RtId { get; }
    
    /// <inheritdoc />
    public RtCkId<CkTypeId> CkTypeId { get; }

    /// <summary>
    ///     MOD option.
    /// </summary>
    [Newtonsoft.Json.JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Include)]
    public EntityModOptions ModOption { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Not serialized — the guard is meaningful only in-process at write time, not on the
    ///     wire. Cross-process consumers should call <see cref="CreateConditionalUpdate" />
    ///     themselves if they need OCC.
    /// </remarks>
    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public AttributeNewerThanGuard? UpdateGuard { get; }

    /// <inheritdoc />
    /// <remarks>
    ///     Serialized (both Newtonsoft and System.Text.Json) so the clear list survives a hop between
    ///     processes; omitted from the wire when not set.
    /// </remarks>
    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyCollection<string>? ClearSecretAttributes { get; }

    /// <inheritdoc />
    public RtEntityId GetRtEntityId()
    {
        if (RtId == null)
        {
            throw new InvalidOperationException("RtId is null");
        }
        
        return new RtEntityId(CkTypeId, RtId.Value);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for insert.
    /// </summary>
    /// <param name="ckTypeId">Type identifier of the construction kit</param>
    /// <param name="rtEntity">Runtime entity to insert</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateInsert(RtCkId<CkTypeId> ckTypeId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(ckTypeId, rtEntity, EntityModOptions.Insert);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for insert.
    /// </summary>
    /// <param name="rtEntity">Runtime entity to insert</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateInsert(TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntity.ToRtEntityId(), rtEntity, EntityModOptions.Insert);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for delete.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateDelete(RtEntityId rtEntityId)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, EntityModOptions.Delete);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for update.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to update</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateUpdate(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Update);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for a
    ///     conditional update guarded by <paramref name="updateGuard" />. The write is
    ///     applied only if the persisted value at <see cref="AttributeNewerThanGuard.AttributePath" />
    ///     is missing, null, or less than or equal to
    ///     <see cref="AttributeNewerThanGuard.NewValue" />. Otherwise the write is silently
    ///     skipped — the caller can detect this via the matched-count returned from the
    ///     repository.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to update</param>
    /// <param name="updateGuard">Guard that gates the write — see <see cref="AttributeNewerThanGuard" /></param>
    public static EntityUpdateInfo<TEntity> CreateConditionalUpdate(RtEntityId rtEntityId, TEntity rtEntity,
        AttributeNewerThanGuard updateGuard)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Update, updateGuard, null);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for update that also clears
    ///     the listed <c>Secret</c> attributes (AB#5532, see <see cref="ClearSecretAttributes" />).
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to update (may carry no attributes when only clearing)</param>
    /// <param name="clearSecretAttributes">Names (PascalCase) of the Secret attributes to clear</param>
    public static EntityUpdateInfo<TEntity> CreateUpdate(RtEntityId rtEntityId, TEntity rtEntity,
        IEnumerable<string>? clearSecretAttributes)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Update, null,
            clearSecretAttributes);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for replace.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to replace</param>
    /// <returns></returns>
    public static EntityUpdateInfo<TEntity> CreateReplace(RtEntityId rtEntityId, TEntity rtEntity)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Replace);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="EntityUpdateInfo{TEntity}" /> for replace that also clears
    ///     the listed <c>Secret</c> attributes (AB#5532). Without a clear entry a replace keeps a stored
    ///     secret the incoming entity omits or sends as <c>""</c>.
    /// </summary>
    /// <param name="rtEntityId">Runtime entity identifier for runtime id and construction kit type</param>
    /// <param name="rtEntity">Runtime entity to replace</param>
    /// <param name="clearSecretAttributes">Names (PascalCase) of the Secret attributes to clear</param>
    public static EntityUpdateInfo<TEntity> CreateReplace(RtEntityId rtEntityId, TEntity rtEntity,
        IEnumerable<string>? clearSecretAttributes)
    {
        return new EntityUpdateInfo<TEntity>(rtEntityId, rtEntity, EntityModOptions.Replace, null,
            clearSecretAttributes);
    }

    private static IReadOnlyCollection<string>? NormaliseClearList(IEnumerable<string>? clearSecretAttributes)
    {
        if (clearSecretAttributes == null)
        {
            return null;
        }

        var list = clearSecretAttributes
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return list.Count == 0 ? null : list.AsReadOnly();
    }
}