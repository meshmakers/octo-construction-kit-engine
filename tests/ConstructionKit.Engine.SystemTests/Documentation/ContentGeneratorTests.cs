using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Xunit;
using Meshmakers.Octo.ConstructionKit.Engine.Documentation;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructionKit.Engine.SystemTests.Documentation;

public class ContentGeneratorTests(TemporaryDirectoryFixture fixture) : IClassFixture<TemporaryDirectoryFixture>
{
    private async Task<(IContentGenerator contentGenerator, string rootPath, CkModelGraph resolvedTypes, CkModelId modelId)>
            SetupTestAsync(string relativePath)
        {
            var currentDirectory = Directory.GetCurrentDirectory();

            while (!currentDirectory.EndsWith("octo-construction-kit-engine"))
            {
                var parentDirectory = Directory.GetParent(currentDirectory);
                if (parentDirectory == null)
                {
                    throw new DirectoryNotFoundException("The specified directory 'octo-construction-kit-engine' was not found in the path.");
                }
                currentDirectory = parentDirectory.FullName;
            }

            var path = Path.Combine(currentDirectory, relativePath);
            await using var stream = File.OpenRead(path);

            await using var serviceProvider = fixture.Services.BuildServiceProvider();

            OperationResult operationResult = new(); // operation result is used to collect errors and warnings.
            var ckYamlSerializer = serviceProvider.GetRequiredService<ICkYamlSerializer>();
            var compiledModelRoot = await ckYamlSerializer.DeserializeCompiledModelRootAsync(stream, path, operationResult);
            var originFileResolver = new OriginFileResolver(path);
            var modelResolver = serviceProvider.GetRequiredService<IModelResolver>();
            var resolvedTypes = await modelResolver.ResolveAsync(compiledModelRoot, originFileResolver, operationResult);

            var contentGenerator = serviceProvider.GetRequiredService<IContentGenerator>();
            var rootPath = fixture.CreateTempDirectory();
            var modelId = new CkModelId("System");

            return (contentGenerator, rootPath, resolvedTypes, modelId);
        }

        [Fact]
        public async Task GenerateAttributesMarkdownTable_ShouldCreateAttributesFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, resolvedTypes, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateAttributesMarkdownTable(resolvedTypes, rootPath, modelId, "", "test");

            // Assert
            Assert.True(Directory.Exists(rootPath));
            Assert.True(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.True(File.Exists(Path.Combine(rootPath, "System", "Attributes.md")));
        }
        
        [Fact]
        public async Task GenerateEnumsMarkdownTable_ShouldNOTCreateEnumsFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, resolvedTypes, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateEnumsMarkdownTable(resolvedTypes, rootPath, modelId, "", "test");

            // Assert
            Assert.False(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.False(File.Exists(Path.Combine(rootPath, "System", "Enums.md")));
        }
        
        [Fact]
        public async Task GenerateRecordsMarkdownTable_ShouldNOTCreateRecordsFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, resolvedTypes, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateRecordsMarkdownTable(resolvedTypes, rootPath, modelId, "", "test");

            // Assert
            Assert.False(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.False(File.Exists(Path.Combine(rootPath, "System", "Records.md")));
        }

        [Fact]
        public async Task GenerateTypesMarkdownTable_ShouldCreateTypesFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, resolvedTypes, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateTypesMarkdownTable(resolvedTypes, rootPath, modelId, "", "test");

            // Assert
            Assert.True(Directory.Exists(rootPath));
            Assert.True(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.True(File.Exists(Path.Combine(rootPath, "System", "Types.md")));
        }
        
        [Fact]
        public async Task GenerateAssociationRolesMarkdownTable_ShouldCreateAssociationsFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, resolvedTypes, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateAssociationRolesMarkdownTable(resolvedTypes, rootPath, modelId, "", "test");

            // Assert
            Assert.True(Directory.Exists(rootPath));
            Assert.True(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.True(File.Exists(Path.Combine(rootPath, "System", "Associations.md")));
        }
        
        [Fact]
        public async Task GenerateVersionHistory_ShouldCreateVersionHistoryFile()
        {
            // Arrange
            const string relativePath = "tests\\TestCkModel\\CkTest\\ConstructionKit\\ckModel.yaml";
            var (contentGenerator, rootPath, _, modelId) = await SetupTestAsync(relativePath);

            // Act
            await contentGenerator.GenerateVersionHistory(rootPath, modelId, "", "test");

            // Assert
            Assert.True(Directory.Exists(rootPath));
            Assert.True(Directory.Exists(Path.Combine(rootPath, modelId.SemanticVersionedFullName)));
            Assert.True(File.Exists(Path.Combine(rootPath, "System", "VersionHistory.md")));
        }
}