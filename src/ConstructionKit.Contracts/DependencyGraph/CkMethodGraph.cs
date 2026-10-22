using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///     A method of a CK type in the dependency graph (CK v2, AB#5669). <see cref="CkTypeGraph.AllMethods" /> holds
///     one entry per method including inherited ones; <see cref="DeclaringCkTypeId" /> is the type that declares the
///     method (the base type for an inherited method).
/// </summary>
/// <param name="DeclaringCkTypeId">The CK type that declares the method</param>
/// <param name="Definition">The method definition as declared in the model</param>
public sealed record CkMethodGraph(CkId<CkTypeId> DeclaringCkTypeId, CkMethodDto Definition)
{
    /// <summary>
    ///     CK v2 (F1.1-S4): the effective visibility of the method (declared, otherwise <c>Public</c>).
    /// </summary>
    [JsonIgnore]
    public CkVisibilityDto Visibility => CkModifiers.ResolveVisibility(Definition.Visibility);

    /// <summary>
    ///     The model-version-less qualified method id (see <see cref="QualifiedMethodId" />).
    /// </summary>
    [JsonIgnore]
    public string QualifiedMethodId => CkMethodIds.Qualify(DeclaringCkTypeId.ToRtCkId(), Definition.MethodId);

    /// <summary>
    ///     The effective timeout (<see cref="CkMethodExecutionDto.TimeoutSeconds" /> or
    ///     <see cref="CkMethodExecutionDto.DefaultTimeoutSeconds" />).
    /// </summary>
    [JsonIgnore]
    public int TimeoutSeconds => Definition.Execution?.TimeoutSeconds ?? CkMethodExecutionDto.DefaultTimeoutSeconds;

    /// <summary>
    ///     The effective handler kind, see <see cref="CkMethodHandlers" /> (AB#6877). <c>pipeline</c>: the deployed
    ///     pipeline named like <see cref="QualifiedMethodId" /> implements the method, no service handler is expected.
    /// </summary>
    [JsonIgnore]
    public string Handler => CkMethodHandlers.Effective(Definition.Execution?.Handler);

    /// <summary>
    ///     AB#7024: true when this static method replaces the generic create of <see cref="DeclaringCkTypeId" /> and
    ///     of every type derived from it (the refusal itself is enforced by the asset repository).
    /// </summary>
    [JsonIgnore]
    public bool ReplacesGenericCreate => Definition.ReplacesGenericCreate;

    /// <summary>
    ///     AB#7006: true when the result of this method is marked sensitive (never logged, audited or traced). False
    ///     for a method without a result.
    /// </summary>
    [JsonIgnore]
    public bool HasSensitiveResult => Definition.Result?.Sensitive == true;

    /// <summary>
    ///     AB#7036: the effective data access of the method — the data permission a caller needs on the target record
    ///     (declared <c>authorization.dataAccess</c>, otherwise <see cref="CkMethodDataAccessDto.Write" />).
    /// </summary>
    [JsonIgnore]
    public CkMethodDataAccessDto DataAccess => CkMethodAuthorization.Resolve(Definition.Authorization);
}
