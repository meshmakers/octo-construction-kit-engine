using System.Text.Json;
using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.ModelCatalogs;

/// <summary>
/// Unit tests for LocalFileSystemCatalog
/// </summary>
public class LocalFileSystemCatalogTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly LocalFileSystemCatalog _repository;
    private readonly ICkJsonSerializer _mockJsonSerializer;

    public LocalFileSystemCatalogTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LocalFileSystemCatalogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _mockJsonSerializer = A.Fake<ICkJsonSerializer>();
        IOptions<LocalFileSystemCatalogOptions> options = Options.Create(new LocalFileSystemCatalogOptions
        {
            CacheDirectory = _tempDirectory,
            RootPath = _tempDirectory
        });

        _repository = new LocalFileSystemCatalog(options, _mockJsonSerializer);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    #region Basic Properties Tests

    [Fact]
    public void Order_ShouldReturn10()
    {
        Assert.Equal(10, _repository.Order);
    }

    [Fact]
    public void RepositoryName_ShouldReturnLocalRepository()
    {
        Assert.Equal("LocalFileSystemCatalog", _repository.CatalogName);
    }

    [Fact]
    public void Description_ShouldContainRootPath()
    {
        Assert.Contains(_tempDirectory, _repository.Description);
    }

    [Fact]
    public void CanWrite_ShouldReturnTrue()
    {
        Assert.True(_repository.CanWrite);
    }

    #endregion

    #region IsSupportingSourceIdentifier Tests

    [Fact]
    public void IsSupportingSourceIdentifier_WithNull_ShouldReturnTrue()
    {
        Assert.True(_repository.IsSupportingSourceIdentifier());
    }

    [Fact]
    public void IsSupportingSourceIdentifier_WithNonNull_ShouldReturnFalse()
    {
        Assert.False(_repository.IsSupportingSourceIdentifier("test"));
    }

    #endregion

    #region IsModelIdExistingAsync Tests

    [Fact]
    public async Task IsModelIdExistingAsync_WithExistingModel_ShouldReturnTrue()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        CreateModelFile(modelId);
        await PublishModel(new CkModelId("TestModel", "1.0.0"), null);

        // Act
        var result = await _repository.IsExistingAsync(modelId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithNonExistingModel_ShouldReturnFalse()
    {
        // Arrange
        var modelId = new CkModelId("NonExistingModel", "1.0.0");

        // Act
        var result = await _repository.IsExistingAsync(modelId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithVersionRange_EmptyDirectory_ShouldReturnNotExists()
    {
        // Arrange
        var modelIdVersionRange = new CkModelIdVersionRange("TestModel", "1.0.0");

        // Act
        var result = await _repository.IsExistingAsync(modelIdVersionRange);

        // Assert
        Assert.False(result.Exists);
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithVersionRange_NoMatchingVersions_ShouldReturnNotExists()
    {
        // Arrange
        var modelIdVersionRange = new CkModelIdVersionRange("TestModel", "2.0.0");
        CreateModelFile(new CkModelId("TestModel", "1.0.0"));

        // Act
        var result = await _repository.IsExistingAsync(modelIdVersionRange);

        // Assert
        Assert.False(result.Exists);
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithVersionRange_MatchingVersion_ShouldReturnExists()
    {
        // Arrange
        var modelIdVersionRange = new CkModelIdVersionRange("TestModel", "1.0.0");
        CreateModelFile(new CkModelId("TestModel", "1.0.0"));
        await PublishModel(new CkModelId("TestModel", "1.0.0"), null);

        // Act
        var result = await _repository.IsExistingAsync(modelIdVersionRange);

        // Assert
        Assert.True(result.Exists);
        Assert.Equal("TestModel", result.ModelId?.Name);
        Assert.Equal("1.0.0", result.ModelId?.Version.ToString());
    }

    [Fact]
    public async Task IsModelIdExistingAsync_WithVersionRange_MultipleVersions_ShouldReturnLatest()
    {
        // Arrange
        var modelIdVersionRange = new CkModelIdVersionRange("TestModel", "[1.0,2.0)");
        CreateModelFile(new CkModelId("TestModel", "1.0.0"));
        CreateModelFile(new CkModelId("TestModel", "1.1.0"));
        CreateModelFile(new CkModelId("TestModel", "1.2.0"));
        await PublishModel(new CkModelId("TestModel", "1.0.0"), null);
        await PublishModel(new CkModelId("TestModel", "1.1.0"), null);
        await PublishModel(new CkModelId("TestModel", "1.2.0"), null);

        // Act
        var result = await _repository.IsExistingAsync(modelIdVersionRange);

        // Assert
        Assert.True(result.Exists);
        Assert.Equal("TestModel", result.ModelId?.Name);
        Assert.Equal("1.2.0", result.ModelId?.Version.ToString());
    }

    #endregion

    #region GetModelAsync Tests

    [Fact]
    public async Task GetModelAsync_WithExistingModel_ShouldReturnModel()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var expectedModel = new CkCompiledModelRoot { ModelId = modelId };
        CreateModelFile(modelId);
        await PublishModel(new CkModelId("TestModel", "1.0.0"), null);

        var operationResult = new OperationResult();
        A.CallTo(() => _mockJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, operationResult, A<bool>._))
            .Returns(Task.FromResult(expectedModel));

        // Act
        var result = await _repository.GetAsync(modelId, operationResult);

        // Assert
        Assert.Equal(expectedModel, result);
        A.CallTo(() => _mockJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, operationResult, A<bool>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetModelAsync_WithNonExistingModel_ShouldThrowModelCatalogException()
    {
        // Arrange
        var modelId = new CkModelId("NonExistingModel", "1.0.0");
        var operationResult = new OperationResult();

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => _repository.GetAsync(modelId, operationResult));

        Assert.Contains("not found", exception.Message);
        Assert.Contains("NonExistingModel-1.0.0", exception.Message);
    }

    [Fact]
    public async Task GetModelAsync_WithDeserializationErrors_ShouldThrowModelCatalogException()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        CreateModelFile(modelId);
        await PublishModel(new CkModelId("TestModel", "1.0.0"), null);

        var operationResult = new OperationResult();
        operationResult.AddMessage(new OperationMessage(MessageLevel.Error, "Test", 1, "Deserialization failed"));

        A.CallTo(() => _mockJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, operationResult, A<bool>._))
            .Returns(Task.FromResult(new CkCompiledModelRoot { ModelId = modelId }));

        // Act & Assert
        var exception =
            await Assert.ThrowsAsync<ModelCatalogException>(() => _repository.GetAsync(modelId, operationResult));

        Assert.Contains("Error", exception.Message);
    }

    #endregion

    #region PublishAsync Tests

    [Fact]
    public async Task PublishModelAsync_NewModel_ShouldCreateFileAndIndexes()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var description = "Test model description";
        var compiledModel = new CkCompiledModelRoot
        {
            ModelId = modelId,
            Description = description
        };

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Returns(Task.CompletedTask);

        // Act
        await _repository.PublishAsync(compiledModel);

        // Assert
        var expectedPath = Path.Combine(_tempDirectory, "ck-models", "v2", "t", "TestModel", "1",
            "ck-testmodel-1.0.0.json");
        Assert.True(File.Exists(expectedPath));

        // Verify catalog was created
        var catalogPath = Path.Combine(_tempDirectory, "ck-models", "v2", "catalog.json");
        Assert.True(File.Exists(catalogPath));

        // Verify model catalog was created
        var modelCatalog = Path.Combine(_tempDirectory, "ck-models", "v2", "t", "TestModel", "catalog.json");
        Assert.True(File.Exists(modelCatalog));

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task PublishModelAsync_ExistingModelWithoutForce_ShouldThrowModelCatalogException()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var compiledModel = new CkCompiledModelRoot { ModelId = modelId };
        CreateModelFile(modelId);
        await _repository.PublishAsync(compiledModel);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => _repository.PublishAsync(compiledModel));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task PublishModelAsync_ExistingModelWithForce_ShouldOverwriteFile()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var compiledModel = new CkCompiledModelRoot { ModelId = modelId };
        CreateModelFile(modelId);

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Returns(Task.CompletedTask);

        // Act
        await _repository.PublishAsync(compiledModel, force: true);

        // Assert
        var expectedPath = Path.Combine(_tempDirectory, "ck-models", "TestModel", "1", "ck-testmodel-1.0.0.json");
        Assert.True(File.Exists(expectedPath));

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task PublishModelAsync_SerializationError_ShouldThrowModelCatalogException()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var compiledModel = new CkCompiledModelRoot { ModelId = modelId };

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Throws(new InvalidOperationException("Serialization failed"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ModelCatalogException>(() => _repository.PublishAsync(compiledModel));

        Assert.Contains("failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishModelAsync_RepublishWithShorterDescription_CatalogRemainsValidJson()
    {
        // Republishing with a shorter description shrinks the per-major catalog.json.
        // If the writer opens the file without truncation, leftover bytes from the
        // previous (longer) write trail the new content and the next read throws
        // `'}' is invalid after a single JSON value`.
        var modelId = new CkModelId("TestModel", "1.0.0");

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, A<CkCompiledModelRoot>._))
            .Returns(Task.CompletedTask);

        var longModel = new CkCompiledModelRoot { ModelId = modelId, Description = new string('x', 500) };
        await _repository.PublishAsync(longModel);

        var catalogPath = Path.Combine(_tempDirectory, "ck-models", "v2", "t", "TestModel", "1", "catalog.json");
        var sizeAfterFirst = new FileInfo(catalogPath).Length;

        var shortModel = new CkCompiledModelRoot { ModelId = modelId, Description = "y" };
        await _repository.PublishAsync(shortModel, force: true);

        var sizeAfterSecond = new FileInfo(catalogPath).Length;
        Assert.True(sizeAfterSecond < sizeAfterFirst,
            $"Test premise broken: expected smaller catalog after replacing description, got {sizeAfterSecond} vs {sizeAfterFirst} bytes.");

        await using var fileStream = File.OpenRead(catalogPath);
        using var doc = await JsonDocument.ParseAsync(fileStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(doc);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public void ListCkModelsAsync_EmptyRepository_ShouldReturnEmptyList()
    {
        // Act
        var result = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListCkModelsAsync_WithModels_ShouldReturnModelsWithDescriptions()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var description = "Test description";
        await PublishModel(modelId, description);

        // Act
        var result = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
        var model = result.First();
        Assert.Equal("LocalFileSystemCatalog", model.CatalogName);
        Assert.Equal("TestModel", model.ModelId.Name);
        Assert.Equal("1.0.0", model.ModelId.Version.ToString());
        Assert.Equal(description, model.Description);
    }

    [Fact]
    public async Task ListCkModelsAsync_WithMultipleModels_ShouldReturnAllModels()
    {
        // Arrange
        await PublishModel(new CkModelId("Model1", "1.0.0"), "Description 1");
        await PublishModel(new CkModelId("Model2", "2.0.0"), "Description 2");

        // Act
        var result = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.ModelId.Name == "Model1");
        Assert.Contains(result, m => m.ModelId.Name == "Model2");
    }

    [Fact]
    public async Task ListCkModelsAsync_WithInvalidVersionInCatalog_ShouldSkipInvalidEntries()
    {
        // Arrange
        await PublishModel(new CkModelId("ValidModel", "1.0.0"), "Valid description");

        // Manually create catalog with invalid version
        var catalog = """
                      {
                        "version": "1.0",
                        "updatedAt": "2023-01-01T00:00:00Z",
                        "models": [
                          {
                            "modelName": "ValidModel",
                            "latestVersion": "1.0.0",
                            "description": "Valid description",
                            "indexPath": "ValidModel/index.json"
                          },
                          {
                            "modelName": "InvalidModel",
                            "latestVersion": "invalid.version",
                            "description": "Invalid description",
                            "indexPath": "InvalidModel/index.json"
                          }
                        ]
                      }
                      """;

        var catalogPath = Path.Combine(_tempDirectory, "ck-models", "catalog.json");
        await File.WriteAllTextAsync(catalogPath, catalog, TestContext.Current.CancellationToken);

        // Act
        var result = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("ValidModel", result.First().ModelId.Name);
    }

    #endregion

    #region SearchAsync Tests

    [Fact]
    public async Task SearchCkModelsAsync_EmptySearchTerm_ShouldReturnAllModels()
    {
        // Arrange
        await PublishModel(new CkModelId("TestModel", "1.0.0"), "Description");

        // Act
        var result = _repository.SearchAsync("", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task SearchCkModelsAsync_NullSearchTerm_ShouldReturnAllModels()
    {
        // Arrange
        await PublishModel(new CkModelId("TestModel", "1.0.0"), "Description");

        // Act
        var result = _repository.SearchAsync(null!, null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
    }

    [Fact]
    public async Task SearchCkModelsAsync_MatchingModelName_ShouldReturnMatchingModels()
    {
        // Arrange
        await PublishModel(new CkModelId("TestModel", "1.0.0"), "Description");
        await PublishModel(new CkModelId("OtherModel", "1.0.0"), "Description");

        // Act
        var result = _repository.SearchAsync("Test", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("TestModel", result.First().ModelId.Name);
    }

    [Fact]
    public async Task SearchCkModelsAsync_MatchingDescription_ShouldReturnMatchingModels()
    {
        // Arrange
        await PublishModel(new CkModelId("Model1", "1.0.0"), "Contains keyword special");
        await PublishModel(new CkModelId("Model2", "1.0.0"), "Normal description");

        // Act
        var result = _repository.SearchAsync("special", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("Model1", result.First().ModelId.Name);
    }

    [Fact]
    public async Task SearchCkModelsAsync_CaseInsensitive_ShouldReturnMatchingModels()
    {
        // Arrange
        await PublishModel(new CkModelId("TestModel", "1.0.0"), "Description");

        // Act
        var result = _repository.SearchAsync("testmodel", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("TestModel", result.First().ModelId.Name);
    }

    [Fact]
    public async Task SearchCkModelsAsync_NoMatches_ShouldReturnEmptyList()
    {
        // Arrange
        await PublishModel(new CkModelId("TestModel", "1.0.0"), "Description");

        // Act
        var result = _repository.SearchAsync("nonexistent", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Helper Methods

    private void CreateModelFile(CkModelId modelId)
    {
        var modelPath = Path.Combine(_tempDirectory, "ck-models", modelId.Name);
        var modelVersionPath = Path.Combine(modelPath, modelId.Version.Major.ToString());
        var compiledModelFile = $"ck-{modelId.Name.ToLower()}-{modelId.Version}.json";
        var compiledModelFilePath = Path.Combine(modelVersionPath, compiledModelFile);

        Directory.CreateDirectory(modelVersionPath);
        File.WriteAllText(compiledModelFilePath, "{}");
    }


    private async Task PublishModel(CkModelId modelId, string? description)
    {
        var compiledModel = new CkCompiledModelRoot
        {
            ModelId = modelId,
            Description = description
        };

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Returns(Task.CompletedTask);

        await _repository.PublishAsync(compiledModel);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task PublishAndRetrieve_FullWorkflow_ShouldWorkCorrectly()
    {
        // Arrange
        var modelId = new CkModelId("TestModel", "1.0.0");
        var description = "Integration test description";
        var compiledModel = new CkCompiledModelRoot
        {
            ModelId = modelId,
            Description = description
        };

        A.CallTo(() => _mockJsonSerializer.SerializeAsync(A<StreamWriter>._, compiledModel))
            .Returns(Task.CompletedTask);
        A.CallTo(() =>
                _mockJsonSerializer.DeserializeCompiledModelRootAsync(A<Stream>._, A<string>._, A<OperationResult>._, A<bool>._))
            .Returns(Task.FromResult(compiledModel));

        // Act - Publish
        await _repository.PublishAsync(compiledModel);

        // Act - Check existence
        var exists = await _repository.IsExistingAsync(modelId);
        var existsWithRange = await _repository.IsExistingAsync(
            new CkModelIdVersionRange("TestModel", "1.0.0"));

        // Act - Retrieve
        var operationResult = new OperationResult();
        var retrievedModel = await _repository.GetAsync(modelId, operationResult);

        // Act - List and Search
        var listedModels = _repository.ListAsync(null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();

        var searchedModels = _repository.SearchAsync("Test", null)
            .ToBlockingEnumerable(cancellationToken: TestContext.Current.CancellationToken).ToList();


        // Assert
        Assert.True(exists);
        Assert.True(existsWithRange.Exists);
        Assert.Equal(compiledModel, retrievedModel);
        Assert.Single(listedModels);
        Assert.Single(searchedModels);
        Assert.Equal(description, listedModels.First().Description);
        Assert.Equal(description, searchedModels.First().Description);
    }

    #endregion
}