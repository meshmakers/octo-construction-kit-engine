using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.DependencyGraph;

/// <summary>
/// Tests for the <c>isRuntimeState</c> marker on <see cref="CkAttributeDto"/>.
/// The flag tags an attribute as carrying runtime state (deployment status,
/// communication status, sync counters, …) that blueprint re-apply must NOT
/// reset. The flag has to propagate from the DTO through both graph
/// representations (<see cref="CkAttributeGraph"/> and
/// <see cref="CkTypeAttributeGraph"/>) so the blueprint runner can consult it
/// at apply time from the CK cache.
/// </summary>
public class CkAttributeIsRuntimeStateTests
{
    private static CkId<CkAttributeId> AttrCkId(string name)
        => new($"Test-1.0.0/{name}");

    [Fact]
    public void CkAttributeDto_IsRuntimeStateDefaultsToFalse_BackwardsCompatible()
    {
        // Existing CK models that don't set the flag must continue to behave
        // exactly as before — every attribute defaults to seed-managed.
        var dto = new CkAttributeDto
        {
            AttributeId = "DeploymentState-1",
            ValueType = AttributeValueTypesDto.Enum,
        };

        Assert.False(dto.IsRuntimeState);
    }

    [Fact]
    public void CkAttributeGraph_FromDto_PropagatesIsRuntimeState()
    {
        var dto = new CkAttributeDto
        {
            AttributeId = "DeploymentState-1",
            ValueType = AttributeValueTypesDto.Enum,
            IsRuntimeState = true,
        };

        var graph = new CkAttributeGraph(AttrCkId("DeploymentState-1"), dto);

        Assert.True(graph.IsRuntimeState);
    }

    [Fact]
    public void CkAttributeGraph_FromDto_DefaultsToFalseWhenDtoUnset()
    {
        var dto = new CkAttributeDto
        {
            AttributeId = "Hostname-1",
            ValueType = AttributeValueTypesDto.String,
        };

        var graph = new CkAttributeGraph(AttrCkId("Hostname-1"), dto);

        Assert.False(graph.IsRuntimeState);
    }

    [Fact]
    public void CkTypeAttributeGraph_FromDtoAndAttributeGraph_PropagatesIsRuntimeState()
    {
        // CkTypeAttributeGraph is built from the per-type attribute reference
        // (CkTypeAttributeDto) plus the global attribute definition
        // (CkAttributeGraph). The runtime-state flag lives on the global
        // definition and must reach the per-type graph node — that's the
        // entry point blueprint apply code consults via the CK cache.
        var attrId = AttrCkId("DeploymentState-1");
        var ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "DeploymentState-1",
            ValueType = AttributeValueTypesDto.Enum,
            IsRuntimeState = true,
        };
        var ckAttributeGraph = new CkAttributeGraph(attrId, ckAttributeDto);
        var ckTypeAttributeDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = "DeploymentState",
        };

        var typeAttributeGraph = new CkTypeAttributeGraph(attrId, ckTypeAttributeDto, ckAttributeGraph);

        Assert.True(typeAttributeGraph.IsRuntimeState);
    }

    [Fact]
    public void CkTypeAttributeGraph_FromDtoAndAttributeGraph_DefaultsToFalseWhenAttributeUnset()
    {
        var attrId = AttrCkId("Hostname-1");
        var ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "Hostname-1",
            ValueType = AttributeValueTypesDto.String,
        };
        var ckAttributeGraph = new CkAttributeGraph(attrId, ckAttributeDto);
        var ckTypeAttributeDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = "Hostname",
        };

        var typeAttributeGraph = new CkTypeAttributeGraph(attrId, ckTypeAttributeDto, ckAttributeGraph);

        Assert.False(typeAttributeGraph.IsRuntimeState);
    }
}
