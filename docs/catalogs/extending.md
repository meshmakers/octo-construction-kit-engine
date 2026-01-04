# Extending the Catalog System

## Implementing a Custom Catalog

### Basic Structure

To implement a custom catalog, implement the `ICatalog` interface:

```csharp
public class CustomCatalog : ICatalog
{
    public int Order => 15; // Priority between Local (10) and GitHub (20)
    public string CatalogName => "custom";
    public string Description => "Custom catalog for special storage";
    public bool CanWrite => true;
    public bool CanRead => true;

    public Task RefreshCatalogAsync(object? sourceIdentifier = null)
    {
        // Refresh cache/index
    }

    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null
            || sourceIdentifier is CustomSourceIdentifier;
    }

    public Task<ModelExistingResult> IsExistingAsync(
        CkModelIdVersionRange modelIdVersionRange,
        object? sourceIdentifier = null)
    {
        // Check if model exists
    }

    public Task<bool> IsExistingAsync(
        CkModelId modelId,
        object? sourceIdentifier = null)
    {
        // Check if exact version exists
    }

    public Task<CkCompiledModelRoot> GetAsync(
        CkModelId modelId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        // Load and deserialize model
    }

    public Task PublishAsync(
        CkCompiledModelRoot ckCompiledModel,
        bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        // Save model
    }

    public IAsyncEnumerable<CatalogResultItem> ListAsync(
        object? sourceIdentifier)
    {
        // List all models
    }

    public IAsyncEnumerable<CatalogResultItem> SearchAsync(
        string searchTerm,
        object? sourceIdentifier)
    {
        // Filter models by search term
    }
}
```

### Using CachedCatalog as Base

For catalogs with caching support, use `CachedCatalog` as the base class:

```csharp
public class CustomCatalog : CachedCatalog
{
    public override int Order => 15;
    public override string CatalogName => "custom";
    public override string Description => "Custom cached catalog";
    public override bool CanWrite => true;

    public CustomCatalog(
        ILogger<CustomCatalog> logger,
        IOptions<CustomCatalogOptions> options)
        : base(logger, options.Value)
    {
    }

    protected override async Task<CacheCatalog> FetchCatalogAsync()
    {
        // Fetch remote catalog and convert to cache format
        var models = await FetchModelsFromStorageAsync();

        return new CacheCatalog
        {
            Models = models.ToDictionary(
                m => m.Name,
                m => new CacheModelEntry
                {
                    Versions = m.Versions.ToDictionary(
                        v => v.ToString(),
                        v => new CacheModelVersionEntry
                        {
                            Path = GetModelPath(m.Name, v),
                            Version = v,
                            Description = m.Description
                        })
                })
        };
    }

    public override async Task<CkCompiledModelRoot> GetAsync(
        CkModelId modelId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var content = await LoadModelContentAsync(modelId);
        return JsonSerializer.Deserialize<CkCompiledModelRoot>(content);
    }

    public override async Task PublishAsync(
        CkCompiledModelRoot model,
        bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        await SaveModelAsync(model);
        await InvalidateCacheAsync();
    }
}
```

---

## Registration

### Via Dependency Injection

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCustomCatalog(
        this IServiceCollection services,
        Action<CustomCatalogOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddTransient<ICatalog, CustomCatalog>();

        return services;
    }
}
```

### Usage

```csharp
services.AddConstructionKitEngine()
    .AddCustomCatalog(options =>
    {
        options.StorageConnectionString = "...";
        options.ContainerName = "ck-models";
    });
```

---

## Example: Azure Blob Storage Catalog

```csharp
public class AzureBlobCatalog : CachedCatalog
{
    private readonly BlobContainerClient _containerClient;

    public override int Order => 25;
    public override string CatalogName => "azure-blob";
    public override string Description => "Azure Blob Storage catalog";
    public override bool CanWrite => true;

    public AzureBlobCatalog(
        ILogger<AzureBlobCatalog> logger,
        IOptions<AzureBlobCatalogOptions> options)
        : base(logger, options.Value)
    {
        var blobServiceClient = new BlobServiceClient(
            options.Value.ConnectionString);
        _containerClient = blobServiceClient
            .GetBlobContainerClient(options.Value.ContainerName);
    }

    protected override async Task<CacheCatalog> FetchCatalogAsync()
    {
        var catalogBlob = _containerClient.GetBlobClient("catalog.json");
        var content = await catalogBlob.DownloadContentAsync();
        return JsonSerializer.Deserialize<CacheCatalog>(
            content.Value.Content.ToString());
    }

    public override async Task<CkCompiledModelRoot> GetAsync(
        CkModelId modelId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var blobName = GetBlobName(modelId);
        var blobClient = _containerClient.GetBlobClient(blobName);

        var response = await blobClient.DownloadContentAsync();
        var content = response.Value.Content.ToString();

        return JsonSerializer.Deserialize<CkCompiledModelRoot>(content);
    }

    public override async Task PublishAsync(
        CkCompiledModelRoot model,
        bool force = false,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var modelId = model.Model.ModelId;
        var blobName = GetBlobName(modelId);
        var blobClient = _containerClient.GetBlobClient(blobName);

        if (!force && await blobClient.ExistsAsync())
        {
            throw ModelCatalogException.ModelAlreadyExists(modelId);
        }

        var json = JsonSerializer.Serialize(model);
        await blobClient.UploadAsync(
            BinaryData.FromString(json),
            overwrite: force);

        await UpdateCatalogIndexAsync(model);
    }

    private string GetBlobName(CkModelId modelId)
    {
        var firstLetter = modelId.Name[0].ToString().ToLower();
        return $"ck-models/v2/{firstLetter}/{modelId.Name.ToLower()}" +
               $"/{modelId.Version.Major}/{modelId}.json";
    }

    private async Task UpdateCatalogIndexAsync(CkCompiledModelRoot model)
    {
        // Update catalog index
        var catalogBlob = _containerClient.GetBlobClient("catalog.json");
        // ...
    }
}
```

---

## Source Identifier

### Custom Source Identifier

```csharp
public class CustomSourceIdentifier
{
    public string Tenant { get; set; }
    public string Environment { get; set; }
}
```

### Usage in Catalog

```csharp
public class MultiTenantCatalog : ICatalog
{
    public bool IsSupportingSourceIdentifier(object? sourceIdentifier = null)
    {
        return sourceIdentifier == null
            || sourceIdentifier is CustomSourceIdentifier;
    }

    public async Task<CkCompiledModelRoot> GetAsync(
        CkModelId modelId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        var identifier = sourceIdentifier as CustomSourceIdentifier;
        var tenant = identifier?.Tenant ?? "default";
        var environment = identifier?.Environment ?? "production";

        var path = $"tenants/{tenant}/{environment}/models/{modelId}.json";
        return await LoadModelAsync(path);
    }
}
```

---

## Testing

### Mock Catalog for Tests

```csharp
public class InMemoryCatalog : ICatalog
{
    private readonly Dictionary<CkModelId, CkCompiledModelRoot> _models = new();

    public int Order => 0;
    public string CatalogName => "in-memory";
    public string Description => "In-memory catalog for testing";
    public bool CanWrite => true;
    public bool CanRead => true;

    public void AddModel(CkCompiledModelRoot model)
    {
        _models[model.Model.ModelId] = model;
    }

    public Task<bool> IsExistingAsync(
        CkModelId modelId,
        object? sourceIdentifier = null)
    {
        return Task.FromResult(_models.ContainsKey(modelId));
    }

    public Task<CkCompiledModelRoot> GetAsync(
        CkModelId modelId,
        OperationResult operationResult,
        object? sourceIdentifier = null,
        CancellationToken? cancellationToken = null)
    {
        if (_models.TryGetValue(modelId, out var model))
        {
            return Task.FromResult(model);
        }
        throw ModelCatalogException.ModelNotFound(modelId);
    }

    // ... additional implementations
}
```

### Test Setup

```csharp
[Fact]
public async Task Should_find_model_in_custom_catalog()
{
    // Arrange
    var catalog = new InMemoryCatalog();
    catalog.AddModel(CreateTestModel("TestModel", "1.0.0"));

    var manager = new CatalogManager(
        Mock.Of<ILogger<CatalogManager>>(),
        new[] { catalog });

    // Act
    var modelId = new CkModelId("TestModel", new Version(1, 0, 0));
    var result = await manager.GetAsync(modelId, new OperationResult());

    // Assert
    Assert.NotNull(result);
    Assert.Equal(modelId, result.Model.ModelId);
}
```

---

## Best Practices

1. **Choose Order wisely**: Local catalogs should have lower order than remote catalogs
2. **Implement caching**: Use `CachedCatalog` as base for remote catalogs
3. **Throw errors correctly**: Use `ModelCatalogException` factory methods
4. **Use async consistently**: Implement all I/O operations asynchronously
5. **Support cancellation**: Pass `CancellationToken` to all downstream calls
6. **Add logging**: Log important operations for debugging
