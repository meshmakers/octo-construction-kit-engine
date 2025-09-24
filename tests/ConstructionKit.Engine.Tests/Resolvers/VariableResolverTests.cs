using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class VariableResolverTests
{
    [Fact]
    public void Resolve_SimpleVariable_ReturnsResolvedValue()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("version", "1.0.0");

        var result = resolver.Resolve("Version: ${version}", "test-location", operationResult);

        Assert.Equal("Version: 1.0.0", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_MultipleVariables_ReturnsAllResolved()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("name", "TestApp");
        resolver.SetVariable("version", "2.0.0");

        var result = resolver.Resolve("${name}-${version}", "test-location", operationResult);

        Assert.Equal("TestApp-2.0.0", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithSpaces_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("value", "test");

        var result = resolver.Resolve("Value: ${ value }", "test-location", operationResult);

        Assert.Equal("Value: test", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_UnknownVariable_ReturnsOriginalAndAddsWarning()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        var result = resolver.Resolve("Value: ${unknown}", "test-location", operationResult);

        Assert.Equal("Value: ${unknown}", result);
        Assert.Single(operationResult.Messages);
        Assert.Equal(62, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_MixedKnownAndUnknownVariables_ResolvesKnownAndWarnsAboutUnknown()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("known", "resolved");

        var result = resolver.Resolve("${known}-${unknown}", "test-location", operationResult);

        Assert.Equal("resolved-${unknown}", result);
        Assert.Single(operationResult.Messages);
        Assert.Equal(62, operationResult.Messages[0].MessageNumber);
    }

    [Fact]
    public void Resolve_NoVariables_ReturnsOriginalString()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        var result = resolver.Resolve("No variables here", "test-location", operationResult);

        Assert.Equal("No variables here", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_EmptyString_ReturnsEmptyString()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        var result = resolver.Resolve("", "test-location", operationResult);

        Assert.Equal("", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void SetVariable_OverwritesExistingVariable()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("value", "first");
        resolver.SetVariable("value", "second");

        var result = resolver.Resolve("${value}", "test-location", operationResult);

        Assert.Equal("second", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithNumbers_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("var123", "value");

        var result = resolver.Resolve("Test: ${var123}", "test-location", operationResult);

        Assert.Equal("Test: value", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithUnderscore_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("my_var", "value");

        var result = resolver.Resolve("Test: ${my_var}", "test-location", operationResult);

        Assert.Equal("Test: value", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithDot_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("app.version", "1.2.3");

        var result = resolver.Resolve("Version: ${app.version}", "test-location", operationResult);

        Assert.Equal("Version: 1.2.3", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithMultipleDots_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("com.example.app.name", "MyApp");

        var result = resolver.Resolve("App: ${com.example.app.name}", "test-location", operationResult);

        Assert.Equal("App: MyApp", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableWithDotsAndUnderscores_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("my_app.config.db_host", "localhost");

        var result = resolver.Resolve("Host: ${my_app.config.db_host}", "test-location", operationResult);

        Assert.Equal("Host: localhost", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_ComplexVariableNameWithAllAllowedCharacters_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("App_v2.config.DB_HOST_123", "db.example.com");

        var result = resolver.Resolve("Database: ${App_v2.config.DB_HOST_123}", "test-location", operationResult);

        Assert.Equal("Database: db.example.com", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_RepeatedVariable_ResolvesAllOccurrences()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("rep", "X");

        var result = resolver.Resolve("${rep} and ${rep} and ${rep}", "test-location", operationResult);

        Assert.Equal("X and X and X", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_NestedBrackets_ResolvesInnerVariable()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("nested", "value");

        var result = resolver.Resolve("${${nested}}", "test-location", operationResult);

        Assert.Equal("${value}", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableValueContainsVariableSyntax_DoesNotRecursivelyResolve()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("var", "${another}");

        var result = resolver.Resolve("${var}", "test-location", operationResult);

        Assert.Equal("${another}", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_ComplexStringWithMultipleVariables_ResolvesAllCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("host", "localhost");
        resolver.SetVariable("port", "8080");
        resolver.SetVariable("protocol", "http");

        var result = resolver.Resolve("${protocol}://${host}:${port}/api", "test-location", operationResult);

        Assert.Equal("http://localhost:8080/api", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_MultipleUnknownVariables_AddsWarningForEach()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        var result = resolver.Resolve("${unknown1} and ${unknown2}", "test-location", operationResult);

        Assert.Equal("${unknown1} and ${unknown2}", result);
        Assert.Equal(2, operationResult.Messages.Count);
        Assert.Contains("unknown1", operationResult.Messages[0].MessageText);
        Assert.Contains("unknown2", operationResult.Messages[1].MessageText);
    }

    [Fact]
    public void Resolve_VariableNameWithMixedCase_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("MyVariable", "value");

        var result = resolver.Resolve("${MyVariable}", "test-location", operationResult);

        Assert.Equal("value", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void SetVariable_EmptyValue_ResolvesToEmptyString()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("empty", "");

        var result = resolver.Resolve("Before${empty}After", "test-location", operationResult);

        Assert.Equal("BeforeAfter", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_LocationPassedToWarningMessage()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();
        const string testLocation = "specific/test/location";

        resolver.Resolve("${unknown}", testLocation, operationResult);

        Assert.Single(operationResult.Messages);
        Assert.Equal(testLocation, operationResult.Messages[0].Location);
    }

    [Fact]
    public void Resolve_VariableAtStartOfString_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("start", "Beginning");

        var result = resolver.Resolve("${start} of string", "test-location", operationResult);

        Assert.Equal("Beginning of string", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_VariableAtEndOfString_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("end", "Ending");

        var result = resolver.Resolve("String ${end}", "test-location", operationResult);

        Assert.Equal("String Ending", result);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Resolve_OnlyVariable_ResolvesCorrectly()
    {
        var resolver = new VariableResolver();
        var operationResult = new OperationResult();

        resolver.SetVariable("only", "SingleValue");

        var result = resolver.Resolve("${only}", "test-location", operationResult);

        Assert.Equal("SingleValue", result);
        Assert.Empty(operationResult.Messages);
    }
}