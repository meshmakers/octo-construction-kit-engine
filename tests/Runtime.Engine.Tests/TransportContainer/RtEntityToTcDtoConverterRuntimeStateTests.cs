using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Engine.TransportContainer;

using FakeItEasy;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.TransportContainer;

/// <summary>
/// Regression tests for Bug #1458 ("Export of RTModels does not work: LastSeen ->
/// CommunicationChangedDate"). Attributes flagged <c>isRuntimeState: true</c> carry volatile,
/// per-tenant live state (communication state timestamps, deployment/configuration status,
/// sync counters, last-error fields). They must never appear in an exported runtime model:
/// <see cref="RtEntityToTcDtoConverter"/> is the export-only RtEntity → transport-container
/// converter, so it is the single point that has to drop them.
/// </summary>
public class RtEntityToTcDtoConverterRuntimeStateTests
{
    private const string TestCkModelId = "System.Communication-1.0.0";
    private const string TestRtModelId = "System.Communication";
    private const string TenantId = "tenant-1";

    [Fact]
    public void Convert_OmitsRuntimeStateAttributes_KeepsRegularAttributes()
    {
        // An Adapter entity with a normal name attribute and a runtime-state communication
        // timestamp. Only the name must survive into the exported transport container.
        var nameAttr = BuildTypeAttr("Name", isRuntimeState: false);
        var runtimeStateAttr = BuildTypeAttr("CommunicationStateTimestamp", isRuntimeState: true);
        var ckCacheService = FakeCacheReturning(BuildType(nameAttr, runtimeStateAttr));
        var converter = new RtEntityToTcDtoConverter(ckCacheService);

        var entity = Entity(
            ("Name", "meshtest Adapter"),
            ("CommunicationStateTimestamp", new DateTime(2026, 5, 18, 0, 2, 1, DateTimeKind.Utc)));

        var dto = converter.Convert(TenantId, entity);

        var exportedNames = dto.Attributes.Select(a => a.Id.ElementId.Name).ToList();
        Assert.Contains("Name", exportedNames);
        Assert.DoesNotContain("CommunicationStateTimestamp", exportedNames);
    }

    [Fact]
    public void Convert_AllRuntimeState_ExportsNoAttributes()
    {
        var ckCacheService = FakeCacheReturning(BuildType(
            BuildTypeAttr("CommunicationState", isRuntimeState: true),
            BuildTypeAttr("DeploymentState", isRuntimeState: true)));
        var converter = new RtEntityToTcDtoConverter(ckCacheService);

        var entity = Entity(("CommunicationState", 2), ("DeploymentState", 0));

        var dto = converter.Convert(TenantId, entity);

        Assert.Empty(dto.Attributes);
    }

    private static ICkCacheService FakeCacheReturning(CkTypeGraph graph)
    {
        var ckCacheService = A.Fake<ICkCacheService>();
        A.CallTo(() => ckCacheService.GetRtCkType(A<string>._, A<RtCkId<CkTypeId>>._)).Returns(graph);
        return ckCacheService;
    }

    private static RtEntity Entity(params (string Name, object Value)[] attrs)
    {
        var entity = new RtEntity
        {
            RtId = OctoObjectId.GenerateNewId(),
            CkTypeId = new RtCkId<CkTypeId>($"{TestRtModelId}/Adapter"),
        };
        foreach (var (name, value) in attrs)
        {
            entity.SetAttributeRawValue(name, value);
        }
        return entity;
    }

    private static CkTypeGraph BuildType(params CkTypeAttributeGraph[] attrs)
    {
        var allAttributes = attrs.ToDictionary(a => a.CkAttributeId, a => a);
        return new CkTypeGraph(
            new CkId<CkTypeId>($"{TestCkModelId}/Adapter"),
            isAbstract: false,
            isFinal: false,
            isCollectionRoot: false,
            baseTypes: Array.Empty<CkGraphTypeInheritance>(),
            derivedFromCkTypeId: null,
            definingCollectionRootCkTypeId: null,
            derivedTypes: Array.Empty<CkGraphTypeInheritance>(),
            definedAttributes: Array.Empty<CkTypeAttributeDto>(),
            allAttributes: allAttributes,
            indexes: Array.Empty<CkTypeIndexDto>(),
            associations: new CkGraphDirectedAssociations(Array.Empty<CkTypeAssociationDto>()),
            description: "test",
            enableChangeStreamPreAndPostImages: false);
    }

    private static CkTypeAttributeGraph BuildTypeAttr(string name, bool isRuntimeState)
    {
        var attrId = new CkId<CkAttributeId>($"{TestCkModelId}/{name}");
        var ckAttrDto = new CkAttributeDto
        {
            AttributeId = name,
            ValueType = AttributeValueTypesDto.String,
            IsRuntimeState = isRuntimeState,
        };
        var ckAttrGraph = new CkAttributeGraph(attrId, ckAttrDto);
        var ckTypeAttrDto = new CkTypeAttributeDto
        {
            CkAttributeId = attrId,
            AttributeName = name,
        };
        return new CkTypeAttributeGraph(attrId, ckTypeAttrDto, ckAttrGraph);
    }
}
