using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.Logging;
using KitchenSink = Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.ckv2KitchenSink.Builder;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     Runs the element, reference and inheritance resolvers over systemFake + a CK v2 model, in the order of
///     <c>ModelResolver.Resolve</c>.
/// </summary>
public abstract class CkV2ResolverTestBase
{
    private readonly ILoggerFactory _loggerFactory;

    protected CkV2ResolverTestBase(ITestOutputHelper output)
    {
        _loggerFactory = LoggerFactory.Create(builder => { builder.AddXUnit(output); });
    }

    protected const string M = KitchenSink.ModelName;

    protected static CkCompiledModelRoot Model() => KitchenSink.Build();

    protected static CkCompiledTypeDto Type(CkCompiledModelRoot model, string name) =>
        model.Types!.Single(t => t.TypeId.Name == name);

    protected CkModelGraph Resolve(CkCompiledModelRoot model, OperationResult operationResult)
    {
        var modelGraph = new CkModelGraph();
        modelGraph.AppendModel(sampleData.systemFake.Builder.Build());
        var originFileResolver = new OriginFileResolver("TEST");
        new ElementResolver().Resolve(model, modelGraph, new VariableResolver(), originFileResolver, operationResult);
        new ReferenceResolver().Resolve(modelGraph, originFileResolver, operationResult);
        new InheritanceResolver(_loggerFactory.CreateLogger<InheritanceResolver>())
            .Resolve(modelGraph, originFileResolver, operationResult);
        return modelGraph;
    }

    /// <summary>Resolves and asserts that every message is an error with the given number.</summary>
    protected IReadOnlyList<OperationMessage> ResolveExpectingOnly(CkCompiledModelRoot model, int messageNumber)
    {
        var operationResult = new OperationResult();
        Resolve(model, operationResult);
        Assert.NotEmpty(operationResult.Messages);
        Assert.All(operationResult.Messages, m =>
        {
            Assert.Equal(MessageLevel.Error, m.MessageLevel);
            Assert.True(messageNumber == m.MessageNumber, $"expected {messageNumber}, got {m.MessageNumber}: {m.MessageText}");
        });
        return operationResult.Messages.ToList();
    }

    protected void ResolveExpectingNoMessages(CkCompiledModelRoot model)
    {
        var operationResult = new OperationResult();
        Resolve(model, operationResult);
        Assert.True(operationResult.Messages.Count == 0, string.Join(Environment.NewLine, operationResult.Messages));
    }
}
