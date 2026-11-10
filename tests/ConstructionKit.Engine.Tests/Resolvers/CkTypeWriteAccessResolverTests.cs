using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     AB#8574: compiler validation of a type's <c>writeAccess</c> block (message 143) and the CK language gate (90).
/// </summary>
public class CkTypeWriteAccessResolverTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    [Theory]
    [InlineData(new[] { "Interactive" }, null)]
    [InlineData(new[] { "Client" }, null)]
    [InlineData(new[] { "Interactive", "Client" }, null)]
    [InlineData(null, new[] { "System.Ai/KnowledgeTools" })]
    [InlineData(new[] { "Interactive" }, new[] { "System.Ai/KnowledgeTools", "Acme_2.Tools/Import_1" })]
    public void Code143_ValidBlock_OK_AndReachesTheGraph(string[]? principals, string[]? channels)
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto
        {
            Principals = principals?.ToList(), Channels = channels?.ToList()
        };

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.True(operationResult.Messages.Count == 0, string.Join(Environment.NewLine, operationResult.Messages));
        var writeAccess = graph.Types[$"{M}/Tag"].WriteAccess;
        Assert.NotNull(writeAccess);
        Assert.Equal(principals, writeAccess.Principals?.ToArray());
        Assert.Equal(channels, writeAccess.Channels?.ToArray());
    }

    [Fact]
    public void Code143_TypeWithoutBlock_HasNoWriteAccessInTheGraph()
    {
        var graph = Resolve(Model(), new OperationResult());

        Assert.Null(graph.Types[$"{M}/Tag"].WriteAccess);
    }

    [Fact]
    public void Code143_BlockWithoutPrincipalsAndChannels_IsRefused()
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto();

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains("/Tag-1", message.MessageText);
        Assert.Contains("neither 'principals' nor 'channels'", message.MessageText);
    }

    [Fact]
    public void Code143_EmptyPrincipalsList_IsRefused()
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto { Principals = [], Channels = ["System.Ai/Tools"] };

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains("'principals' is an empty list", message.MessageText);
    }

    [Fact]
    public void Code143_EmptyChannelsList_IsRefused()
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto { Principals = ["Interactive"], Channels = [] };

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains("'channels' is an empty list", message.MessageText);
    }

    [Theory]
    [InlineData("interactive")]
    [InlineData("User")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void Code143_UnknownPrincipal_IsRefused(string principal)
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto { Principals = [principal] };

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains($"unknown principal kind '{principal}'", message.MessageText);
    }

    [Theory]
    [InlineData("KnowledgeTools")]
    [InlineData("System.Ai/")]
    [InlineData("/KnowledgeTools")]
    [InlineData("System.Ai/Knowledge/Tools")]
    [InlineData("System Ai/Tools")]
    [InlineData("1System/Tools")]
    [InlineData("claude-agent-gerald-wks-1")]
    public void Code143_InvalidChannel_IsRefused(string channel)
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto { Channels = [channel] };

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains($"invalid channel '{channel}'", message.MessageText);
    }

    [Fact]
    public void Code143_DuplicateEntry_IsRefused()
    {
        var model = Model();
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto
        {
            Principals = ["Interactive", "Interactive"], Channels = ["System.Ai/Tools"]
        };

        var message = Assert.Single(ResolveExpectingOnly(model, 143));
        Assert.Contains("'Interactive' is listed more than once", message.MessageText);
    }

    [Fact]
    public void Code90_BlockInACkLanguage1Model()
    {
        var model = Model();
        model.CkLanguage = 1;
        Type(model, "Tag").WriteAccess = new CkTypeWriteAccessDto { Principals = ["Interactive"] };

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Contains(operationResult.Messages,
            m => m.MessageNumber == 90 && m.MessageText.Contains("'writeAccess'") && m.MessageText.Contains("/Tag-1"));
    }
}
