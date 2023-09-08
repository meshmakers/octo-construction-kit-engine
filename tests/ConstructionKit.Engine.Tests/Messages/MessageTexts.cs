using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Messages;

public class MessageTexts
{
    [Fact]
    public void CreateMessageWithArgument()
    {
        var template = new CompilerMessageTemplate(MessageLevel.Error, 1, "Test '{demo}'", new []{"demo"});
        var message = template.CreateMessage("Test");
        Assert.Equal("Test 'Test'", message.MessageText);
    }
    
    [Fact]
    public void CreateMessageWithOutArgument()
    {
        var template = new CompilerMessageTemplate(MessageLevel.Error, 1, "Test", new string[] { });
        var message = template.CreateMessage("Test");
        Assert.Equal("Test", message.MessageText);
    }
}