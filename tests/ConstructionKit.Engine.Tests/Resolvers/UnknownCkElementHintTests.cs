using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.systemFake;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#6987: the hint of an unknown association target / type / record id tells a missing dependency apart from a
///     wrong element name in a resolved model and from a wrong model prefix. Message numbers are unchanged.
/// </summary>
public class UnknownCkElementHintTests
{
    private const string DependencyMissingHint = "a dependency to another construction kit model is missing";

    private readonly ILoggerFactory _loggerFactory;

    public UnknownCkElementHintTests(ITestOutputHelper output)
    {
        _loggerFactory = LoggerFactory.Create(builder => builder.AddXUnit(output));
    }

    // Demo3 of the fixture targets 'System/EntityNotExisting'; the tests re-point that target.
    private (OperationResult Result, ModelValidationException Exception) ResolveWithTarget(string targetCkTypeId)
    {
        var sample = sampleData.sample_assocs_invalidTarget_fail.Builder.Build();
        sample.Types!.Single(t => t.TypeId == "Demo3").Associations![0].TargetCkTypeId = targetCkTypeId;

        CkModelGraph graph = new();
        graph.AppendModel(Builder.Build());
        graph.AppendModel(sample);

        OperationResult operationResult = new();
        InheritanceResolver resolver = new(_loggerFactory.CreateLogger<InheritanceResolver>());
        var exception = Assert.Throws<ModelValidationException>(() =>
            resolver.Resolve(graph, new OriginFileResolver("TEST"), operationResult));

        Assert.Single(operationResult.Messages);
        Assert.Equal(MessageLevel.FatalError, operationResult.Messages[0].MessageLevel);
        Assert.Equal(18, operationResult.Messages[0].MessageNumber);
        return (operationResult, exception);
    }

    [Fact]
    public void AssociationTarget_ModelResolvedTypeNameWrong_NamesModelAndSuggestsClosestType()
    {
        var (result, exception) = ResolveWithTarget("System/Entty");

        foreach (var text in new[] { result.Messages[0].MessageText, exception.Message })
        {
            Assert.Contains("does not exist in model System-1.0.0", text);
            Assert.Contains("Did you mean 'Entity'?", text);
            Assert.DoesNotContain(DependencyMissingHint, text);
        }
    }

    [Fact]
    public void AssociationTarget_TypeExistsInOtherModel_NamesTheModelThatHoldsIt()
    {
        var (result, exception) = ResolveWithTarget("sample1/Entity");

        foreach (var text in new[] { result.Messages[0].MessageText, exception.Message })
        {
            Assert.Contains("does not exist in model sample1-1.0.0", text);
            Assert.Contains("exists in System-1.0.0", text);
            Assert.DoesNotContain(DependencyMissingHint, text);
        }
    }

    [Fact]
    public void AssociationTarget_ModelNotResolved_KeepsDependencyMissingHint()
    {
        var (result, exception) = ResolveWithTarget("NotLoaded/Entity");

        Assert.Contains(DependencyMissingHint, result.Messages[0].MessageText);
        Assert.Contains(DependencyMissingHint, exception.Message);
        Assert.DoesNotContain("does not exist in model", exception.Message);
    }

    [Fact]
    public void AssociationTarget_RoleIdAndTargetAreNotSwapped()
    {
        var (result, _) = ResolveWithTarget("NotLoaded/Entity");

        Assert.Contains("target CkTypeId 'NotLoaded-1.0.0/Entity-1'", result.Messages[0].MessageText);
        Assert.Contains("for role id 'sample1-1.0.0/Related-1'", result.Messages[0].MessageText);
    }

    [Fact]
    public void AssociationTarget_TypeExists_NoMessage()
    {
        var sample = sampleData.sample_assocs_invalidTarget_fail.Builder.Build();
        sample.Types!.Single(t => t.TypeId == "Demo3").Associations![0].TargetCkTypeId = "System/Entity";
        CkModelGraph graph = new();
        graph.AppendModel(Builder.Build());
        graph.AppendModel(sample);

        OperationResult operationResult = new();
        new InheritanceResolver(_loggerFactory.CreateLogger<InheritanceResolver>())
            .Resolve(graph, new OriginFileResolver("TEST"), operationResult);

        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Hint_UnknownType_Cases()
    {
        CkModelGraph graph = new();
        graph.AppendModel(Builder.Build());
        graph.AppendModel(sampleData.sample1.Builder.Build());

        Assert.Equal(UnknownCkElementHint.DependencyMissing,
            UnknownCkElementHint.ForType(graph, new CkId<CkTypeId>("Missing-1.0.0/Entity-1")));
        Assert.Contains("does not exist in model System-1.0.0",
            UnknownCkElementHint.ForType(graph, new CkId<CkTypeId>("System-1.0.0/Pump-1")));
        Assert.DoesNotContain("Did you mean",
            UnknownCkElementHint.ForType(graph, new CkId<CkTypeId>("System-1.0.0/Pump-1")));
        Assert.Contains("exists in sample1-1.0.0",
            UnknownCkElementHint.ForType(graph, new CkId<CkTypeId>("System-1.0.0/Demo1-1")));
    }

    [Fact]
    public void Hint_UnknownRecord_Cases()
    {
        CkModelGraph graph = new();
        graph.AppendModel(Builder.Build());
        graph.AppendModel(sampleData.sample1.Builder.Build());

        Assert.Equal(UnknownCkElementHint.DependencyMissing,
            UnknownCkElementHint.ForRecord(graph, new CkId<CkRecordId>("Missing-1.0.0/Record1-1")));
        var hint = UnknownCkElementHint.ForRecord(graph, new CkId<CkRecordId>("sample1-1.0.0/Recrd1-1"));
        Assert.Contains("The record 'Recrd1' does not exist in model sample1-1.0.0", hint);
        Assert.Contains("Did you mean 'Record1'?", hint);
    }
}
