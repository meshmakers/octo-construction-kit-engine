using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Documentation;

public class LinkHelpersTest
{
    [Fact]
    public void GetCommonPathParts_ReturnsCorrectPath_ForSinglePath()
    {
        // Arrange
        var linkHelpers = new LinkHelpers();
        var modelId = new CkModelId("Basic");

        // Act
        var result = linkHelpers.GetCommonPathParts(modelId);

        // Assert
        Assert.Equal("Basic", result);
    }

    [Fact]
    public void GetCommonPathParts_ReturnsCorrectPath()
    {
        // Arrange
        var linkHelpers = new LinkHelpers();
        var modelId = new CkModelId("Basic.Industry");

        // Act
        
        var result = linkHelpers.GetCommonPathParts(modelId);

        // Assert

        Assert.Equal("Basic\\Industry", result);
    }
    
    [Fact]
    public void GetCommonPathParts_ReturnsEmptyPath_ForEmptyString()
    {
        // Arrange
        var linkHelpers = new LinkHelpers();
        var modelId = new CkModelId("");

        // Act
        var result = linkHelpers.GetCommonPathParts(modelId);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatAnchor_ReturnsCorrectAnchor()
    {
        //Arrange
        var linkHelpers = new LinkHelpers();
        const string unformattedAnchor = "Industry.Machine";
        
        //Act
        var result = linkHelpers.FormatAnchor(unformattedAnchor);
        
        //Assert
        Assert.Equal("industrymachine", result);
    }

    // [Theory]
    // [InlineData("Basic", "Types", "/docs", "/docs/Basic/Types")]
    // [InlineData("Basic.Industry", "Types", "/docs/meshmakers", "/docs/meshmakers/Basic/Industry/Types")]
    // [InlineData("", "", "", "")]
    // [InlineData("Basic", "", "", "Basic")]
    // [InlineData("", "Types", "", "Types")]
    // [InlineData("Basic", "Types", "", "Basic/Types")]
    // public void CreateRelativeFilepath_ReturnsCorrectFilepath(string ckModelId, string suffix, string baseRelativePath, string expected)
    // {
    //     //Arrange
    //     var linkHelpers = new LinkHelpers();
    //     //Act
    //     var result = linkHelpers.CreateRelativeFilepath(ckModelId, suffix, baseRelativePath);
    //     //Assert
    //     Assert.Equal(expected, result);
    // }

    // [Theory]
    // [InlineData("C:\\octo-documentation\\src\\octo-mesh-documentation\\" +
    //             "docs\\technologyGuide\\constructionKits", "Basic", "Types"
    //     , "C:\\octo-documentation\\src\\octo-mesh-documentation\\" +
    //       "docs\\technologyGuide\\constructionKits\\Basic\\Types.md")]
    // [InlineData("D:\\octo-mesh-documentation\\" +
    //             "docs\\technologyGuide\\constructionKits", "Basic", "Associations"
    //     , "D:\\octo-mesh-documentation\\" +
    //       "docs\\technologyGuide\\constructionKits\\Basic\\Associations.md")]
    // public void GetGeneratedFilepath_ReturnCorrectFilepath(string path, string ckModelId, string extension, string expected)
    // {
    //     //Arrange
    //     var modelId = new CkModelId(ckModelId);
    //     var linkHelpers = new LinkHelpers();
    //     //Act
    //     var result = linkHelpers.GetGeneratedFilePath(path, modelId, extension);
    //     //Assert
    //     Assert.Equal(expected, result);
    // }

    // [Theory]
    // [InlineData("", "Basic", "Associations")]
    // [InlineData("", "", "Associations")]
    // [InlineData("", "", "Types")]
    // [InlineData("C:\\src\\", "", "")]
    // public void GetGeneratedFilepath_InvalidPathArguments(string path, string ckModelId, string extension)
    // {
    //     //Arrange
    //     var modelId = new CkModelId(ckModelId);
    //     var linkHelpers = new LinkHelpers();
    //     //Act & Assert
    //     Assert.Throws<ArgumentException>(() => linkHelpers.GetGeneratedFilePath(path, modelId, extension));
    // }
   
}