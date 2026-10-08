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
}
