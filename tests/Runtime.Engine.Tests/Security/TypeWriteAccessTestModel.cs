using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Security;

/// <summary>
///     AB#8574: a CK cache fake with restricted and unrestricted types for the <c>writeAccess</c> tests.
///     <list type="bullet">
///         <item><c>Test/Knowledge</c>: principals [Interactive], channels [System.Ai/KnowledgeTools]</item>
///         <item><c>Test/SpecialKnowledge</c>: derives from Knowledge, declares nothing (inherits)</item>
///         <item><c>Test/LockedKnowledge</c>: derives from Knowledge, declares channels [System.Ai/KnowledgeTools] (both blocks apply)</item>
///         <item><c>Test/Note</c>: unrestricted</item>
///     </list>
/// </summary>
internal static class TypeWriteAccessTestModel
{
    internal const string Model = "Test-1.0.0";
    internal const string KnowledgeType = "Test/Knowledge";
    internal const string SpecialKnowledgeType = "Test/SpecialKnowledge";
    internal const string LockedKnowledgeType = "Test/LockedKnowledge";
    internal const string NoteType = "Test/Note";
    internal const string Channel = "System.Ai/KnowledgeTools";

    internal static RtCkId<CkTypeId> Rt(string id) => new(id);

    private static CkId<CkTypeId> Ck(string rtId) => new(rtId.Replace("Test/", $"{Model}/"));

    internal static CkTypeAttributeGraph NameAttr()
    {
        var attrId = new CkId<CkAttributeId>($"{Model}/Name");
        var definition = new CkAttributeGraph(attrId,
            new CkAttributeDto { AttributeId = "Name", ValueType = AttributeValueTypesDto.String });
        return new CkTypeAttributeGraph(attrId,
            new CkTypeAttributeDto { CkAttributeId = attrId, AttributeName = "Name", IsOptional = true }, definition);
    }

    private static CkTypeGraph Graph(string rtId, CkTypeWriteAccessDto? writeAccess, string? baseRtId = null)
    {
        var name = NameAttr();
        var baseTypes = baseRtId == null
            ? new List<CkGraphTypeInheritance>()
            : [new CkGraphTypeInheritance(Ck(rtId), Ck(baseRtId), 0)];
        return new CkTypeGraph(Ck(rtId), isAbstract: false, isFinal: false, isCollectionRoot: false,
            baseTypes: baseTypes, derivedFromCkTypeId: baseRtId == null ? null : Ck(baseRtId),
            definingCollectionRootCkTypeId: null, derivedTypes: [], definedAttributes: [],
            allAttributes: new Dictionary<CkId<CkAttributeId>, CkTypeAttributeGraph> { [name.CkAttributeId] = name },
            indexes: [], associations: new CkGraphDirectedAssociations([]), description: "test",
            enableChangeStreamPreAndPostImages: false, writeAccess: writeAccess);
    }

    /// <summary>Registers the four types of the model in <paramref name="ckCache" /> for <paramref name="tenantId" />.</summary>
    /// <param name="ckCache">A FakeItEasy fake</param>
    /// <param name="tenantId">The tenant</param>
    /// <param name="restricted">False registers the same types without any block (regression: unrestricted tenant)</param>
    internal static void Register(ICkCacheService ckCache, string tenantId, bool restricted = true)
    {
        var graphs = new List<CkTypeGraph>
        {
            Graph(KnowledgeType, restricted
                ? new CkTypeWriteAccessDto { Principals = [CkWritePrincipals.Interactive], Channels = [Channel] }
                : null),
            Graph(SpecialKnowledgeType, null, KnowledgeType),
            Graph(LockedKnowledgeType, restricted ? new CkTypeWriteAccessDto { Channels = [Channel] } : null,
                KnowledgeType),
            Graph(NoteType, null)
        };

        foreach (var graph in graphs)
        {
            var rtId = graph.CkTypeId.ToRtCkId();
            var graphOut = (CkTypeGraph?)graph;
            A.CallTo(() => ckCache.GetRtCkType(tenantId, rtId)).Returns(graph);
            A.CallTo(() => ckCache.TryGetRtCkType(tenantId, rtId, out graphOut)).Returns(true)
                .AssignsOutAndRefParameters(graph);
            A.CallTo(() => ckCache.TryGetCkType(tenantId, graph.CkTypeId, out graphOut)).Returns(true)
                .AssignsOutAndRefParameters(graph);
        }

        A.CallTo(() => ckCache.GetCkTypes(tenantId)).Returns(graphs);
    }
}
