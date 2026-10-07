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
    ///     The model-version-less qualified method id, e.g. <c>System.Identity/User.ChangePassword-1</c>
    ///     (see <see cref="CkMethodIds.Qualify" />).
    /// </summary>
    [JsonIgnore]
    public string QualifiedMethodId => CkMethodIds.Qualify(DeclaringCkTypeId.ToRtCkId(), Definition.MethodId);

    /// <summary>
    ///     The effective timeout (<see cref="CkMethodExecutionDto.TimeoutSeconds" /> or
    ///     <see cref="CkMethodExecutionDto.DefaultTimeoutSeconds" />).
    /// </summary>
    [JsonIgnore]
    public int TimeoutSeconds => Definition.Execution?.TimeoutSeconds ?? CkMethodExecutionDto.DefaultTimeoutSeconds;
}
