using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     Real engine services against a <c>LocalFileSystemCatalog</c> in a temp directory (no network, no
///     remote catalogs). Writes small CK sources and compiles/publishes them, so dependency resolution
///     runs end to end exactly as in <c>CkCompile</c>.
/// </summary>
internal sealed class CkCompileFixture : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public CkCompileFixture(Action<IServiceCollection>? configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), $"CkCompileFixture_{Guid.NewGuid():N}");
        CatalogDir = Path.Combine(Root, "catalog");
        Directory.CreateDirectory(CatalogDir);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConstructionKit();
        services.Configure<LocalFileSystemCatalogOptions>(options =>
        {
            options.ApplyRootPath(CatalogDir);
            options.IsEnabled = true;
        });
        services.Configure<PublicGitHubCatalogOptions>(options => options.IsEnabled = false);
        services.Configure<PrivateGitHubCatalogOptions>(options => options.IsEnabled = false);
        // Review G3 E-L6: pin the flag — CkCompilerOptions.RangeRetention defaults from the OctoCkRangeRetention
        // environment variable, which must not change what the v1 tests (incl. the golden test) compile.
        services.Configure<CkCompilerOptions>(o => o.RangeRetention = false);
        configure?.Invoke(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    public string Root { get; }

    public string CatalogDir { get; }

    public IServiceProvider Services => _serviceProvider;

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }

    /// <summary>
    ///     Writes a CK source directory. <paramref name="files" /> maps a relative path
    ///     (e.g. <c>types/thing.yaml</c>) to its YAML body without the schema line.
    /// </summary>
    public string WriteSource(string name, string modelId, IEnumerable<string>? dependencies,
        IDictionary<string, string> files, int? ckLanguage = null, string? extraMetadata = null)
    {
        var dir = Path.Combine(Root, "src", name);
        Directory.CreateDirectory(dir);
        var deps = dependencies?.ToList() ?? [];
        var dependencyBlock = deps.Count == 0
            ? ""
            : "dependencies:\n" + string.Concat(deps.Select(d => $"  - {d}\n"));
        File.WriteAllText(Path.Combine(dir, "ckModel.yaml"),
            "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-meta.schema.json\"\n" +
            $"modelId: {modelId}\n" + (ckLanguage == null ? "" : $"ckLanguage: {ckLanguage}\n") + dependencyBlock +
            extraMetadata);

        foreach (var (relativePath, body) in files)
        {
            var path = Path.Combine(dir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,
                "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n" + body);
        }

        return dir;
    }

    public async Task<CkCompiledModelRoot> CompileAsync(string sourceDir)
    {
        var operationResult = new OperationResult();
        var compiled = await _serviceProvider.GetRequiredService<ICompilerService>()
            .CompileInMemoryAsync(sourceDir, operationResult);
        Assert.False(operationResult.HasErrors, string.Join(Environment.NewLine, operationResult.Messages));
        return compiled;
    }

    public async Task<CkCompiledModelRoot> CompileAndPublishAsync(string sourceDir)
    {
        var compiled = await CompileAsync(sourceDir);
        await _serviceProvider.GetRequiredService<ICatalogService>().PublishAsync(
            LocalFileSystemCatalog.Name, compiled, new OriginFileResolver(sourceDir), isForced: true);
        return compiled;
    }

    /// <summary>A minimal "System" base model (the only model whose root type may lack a base, see CompilerStatics.WhiteListedCkTypeIds): one abstract base type with one attribute (and optionally more).</summary>
    public string WriteSystemModel(string version, bool withExtraAttribute = false)
    {
        var attributes = "attributes:\n  - id: Name\n    valueType: String\n" +
                         (withExtraAttribute ? "  - id: Extra\n    valueType: String\n" : "");
        return WriteSource($"system-{version}", $"System-{version}", null, new Dictionary<string, string>
        {
            ["attributes/attributes.yaml"] = attributes,
            ["types/entity.yaml"] = "types:\n  - typeId: Entity\n    isAbstract: true\n    attributes:\n" +
                                     "      - id: ${this}/Name\n        name: Name\n        isOptional: true\n"
        });
    }
}
