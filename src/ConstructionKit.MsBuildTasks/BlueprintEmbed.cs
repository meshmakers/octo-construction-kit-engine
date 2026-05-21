using System.Text;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
/// MSBuild task that discovers blueprint folders inside the consuming project, validates each
/// <c>blueprint.yaml</c> against the bundled <c>blueprint-meta.schema.json</c>, registers all
/// blueprint files as <c>EmbeddedResource</c> items with deterministic <c>LogicalName</c>
/// metadata, and emits a <c>blueprints-cache.json</c> inventory that the BlueprintSourceGenerator
/// consumes to produce <c>IBlueprintEmbeddedSource</c> implementations + DI extension methods.
///
/// Expected folder layout under each <c>&lt;BlueprintFolder&gt;</c> item:
/// <code>
/// Blueprints/
///   ├── MyBlueprint-1.0.0/
///   │     ├── blueprint.yaml
///   │     └── seed-data/entities.yaml
///   └── OtherBlueprint-2.1.0/
///         ├── blueprint.yaml
///         └── migrations/from-2.0.0.yaml
/// </code>
/// The version folder name <c>&lt;Name&gt;-&lt;Version&gt;</c> is the source of truth; the manifest's
/// <c>blueprintId</c> must agree. Mismatches fail the build.
/// </summary>
public class BlueprintEmbed : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Root folders to scan for blueprint subdirectories. Each <c>%(Identity)</c> is treated as the
    /// parent of one or more <c>&lt;Name&gt;-&lt;Version&gt;</c> blueprint directories.
    /// </summary>
    [Required]
    public ITaskItem[] BlueprintFolders { get; set; } = null!;

    /// <summary>
    /// Root namespace of the consuming project. Used to build the resource namespace of each
    /// blueprint: <c>{RootNamespace}.{BlueprintFolderName}.{Name}-{Version}</c>.
    /// </summary>
    [Required]
    public string RootNamespace { get; set; } = null!;

    /// <summary>
    /// Path where the inventory cache file is written. Source generator picks it up via
    /// <c>&lt;AdditionalFiles&gt;</c>.
    /// </summary>
    [Required]
    public string CacheFilePath { get; set; } = null!;

    /// <summary>
    /// Embedded-resource items produced by the task. Each item's <c>Identity</c> is the absolute
    /// path to a blueprint file, and the <c>LogicalName</c> metadata is set so the runtime catalog
    /// can locate it via <c>Assembly.GetManifestResourceStream</c>.
    /// </summary>
    [Output]
    public ITaskItem[] EmbeddedResourceItems { get; set; } = null!;

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            var schemaValidator = new BlueprintSchemaValidator();
            var yamlDeserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var cache = new BlueprintsCacheDto
            {
                Schema = "https://schemas.meshmakers.cloud/blueprints-cache.schema.json",
                Version = "1"
            };
            var embeddedItems = new List<ITaskItem>();

            foreach (var folderItem in BlueprintFolders)
            {
                var folderPath = folderItem.GetMetadata("FullPath");
                if (!Directory.Exists(folderPath))
                {
                    Log.LogMessage(MessageImportance.Low,
                        "BlueprintEmbed: folder '{0}' does not exist; skipping.", folderPath);
                    continue;
                }

                Log.LogMessage(MessageImportance.High, "Embedding blueprints from '{0}'", folderPath);

                var folderName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));

                foreach (var blueprintDir in Directory.GetDirectories(folderPath))
                {
                    if (!TryProcessBlueprintDirectory(blueprintDir, folderName, schemaValidator,
                            yamlDeserializer, cache, embeddedItems))
                    {
                        return false;
                    }
                }
            }

            // Sort blueprints for deterministic output (alphabetical by name, ascending by version).
            cache.Blueprints = cache.Blueprints
                .OrderBy(b => b.Name, StringComparer.Ordinal)
                .ThenBy(b => b.Version, StringComparer.Ordinal)
                .ToList();

            WriteCacheFile(cache);
            EmbeddedResourceItems = embeddedItems.ToArray();

            Log.LogMessage(MessageImportance.High,
                "BlueprintEmbed: embedded {0} blueprint(s) — cache at '{1}'.",
                cache.Blueprints.Count, CacheFilePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private bool TryProcessBlueprintDirectory(
        string blueprintDir,
        string parentFolderName,
        BlueprintSchemaValidator schemaValidator,
        IDeserializer yamlDeserializer,
        BlueprintsCacheDto cache,
        List<ITaskItem> embeddedItems)
    {
        var manifestPath = Path.Combine(blueprintDir, "blueprint.yaml");
        if (!File.Exists(manifestPath))
        {
            Log.LogMessage(MessageImportance.Low,
                "BlueprintEmbed: '{0}' has no blueprint.yaml; skipping.", blueprintDir);
            return true;
        }

        // 1. Schema-validate the manifest YAML against blueprint-meta.schema.json before reading
        //    any structured value. Catches typos / missing required fields with a clear error at
        //    build time, before the source generator gets fed a half-shaped DTO.
        var operationResult = new OperationResult();
        using (var validateStream = File.OpenRead(manifestPath))
        {
            schemaValidator.ValidateMetaInYaml(validateStream, manifestPath, operationResult);
        }

        if (operationResult.HasErrors)
        {
            foreach (var msg in operationResult.Messages.Where(m => m.MessageLevel == MessageLevel.Error))
            {
                Log.LogError("{0}: {1}", manifestPath, msg.MessageText);
            }
            return false;
        }

        // 2. Read the minimum fields we need with raw YamlDotNet — we only care about blueprintId
        //    (required by the schema, so present after validation) and description (optional).
        ManifestShape manifest;
        try
        {
            var yamlContent = File.ReadAllText(manifestPath, Encoding.UTF8);
            manifest = yamlDeserializer.Deserialize<ManifestShape>(yamlContent)
                       ?? throw new InvalidOperationException("Manifest deserialised to null.");
        }
        catch (Exception ex)
        {
            Log.LogError("Blueprint manifest '{0}' failed to read: {1}", manifestPath, ex.Message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.BlueprintId))
        {
            // The schema already requires this — defensive check in case the schema validation path
            // ever loosens.
            Log.LogError("Blueprint manifest '{0}' is missing required 'blueprintId'.", manifestPath);
            return false;
        }

        var blueprintId = new BlueprintId(manifest.BlueprintId!);

        // 3. The directory name must match the manifest's blueprintId so runtime resolution stays
        //    unambiguous (the runtime locates a blueprint via {ResourceNamespace}.blueprint.yaml).
        var expectedDirName = blueprintId.FullName;
        var actualDirName = Path.GetFileName(blueprintDir);
        if (!string.Equals(expectedDirName, actualDirName, StringComparison.Ordinal))
        {
            Log.LogError(
                "Blueprint directory name '{0}' does not match its blueprintId '{1}'. Rename the folder so the on-disk identity agrees with the manifest.",
                actualDirName, expectedDirName);
            return false;
        }

        // 4. Build the resource namespace. Convention:
        //      {RootNamespace}.{ParentFolderName}.{Name}-{Version}
        //    The parent folder name is included so several <BlueprintFolder> items can coexist
        //    without clashing — each folder gets its own segment in the resource path.
        var resourceNamespace = $"{RootNamespace}.{parentFolderName}.{blueprintId.FullName}";

        // 5. Collect every file under the blueprint root and emit an EmbeddedResource item per file
        //    with a deterministic LogicalName so the runtime catalog can build the same key.
        var files = new List<string>();
        foreach (var filePath in Directory.GetFiles(blueprintDir, "*", SearchOption.AllDirectories))
        {
            var relative = GetRelativePath(blueprintDir, filePath).Replace('\\', '/');
            files.Add(relative);

            var logicalName = $"{resourceNamespace}.{relative.Replace('/', '.')}";

            var item = new TaskItem(filePath);
            item.SetMetadata("LogicalName", logicalName);
            item.SetMetadata("Visible", "False");
            embeddedItems.Add(item);
        }

        cache.Blueprints.Add(new BlueprintsCacheEntryDto
        {
            Name = blueprintId.Name,
            Version = blueprintId.Version.ToString(),
            Description = manifest.Description,
            ResourceNamespace = resourceNamespace,
            Files = files.OrderBy(f => f, StringComparer.Ordinal).ToList()
        });

        Log.LogMessage(MessageImportance.Normal,
            "  + {0} ({1} file(s)) -> {2}",
            blueprintId.FullName, files.Count, resourceNamespace);

        return true;
    }

    private void WriteCacheFile(BlueprintsCacheDto cache)
    {
        var directory = Path.GetDirectoryName(CacheFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
        }

        var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(CacheFilePath, json, new UTF8Encoding(false));
    }

    private static string GetRelativePath(string fromDir, string toPath)
    {
#if NETSTANDARD2_0
        var fromTrim = fromDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fromUri = new Uri(fromTrim);
        var toUri = new Uri(toPath);
        var relUri = fromUri.MakeRelativeUri(toUri);
        return Uri.UnescapeDataString(relUri.ToString().Replace('/', Path.DirectorySeparatorChar));
#else
        return Path.GetRelativePath(fromDir, toPath);
#endif
    }

    /// <summary>
    /// Minimal POCO used by YamlDotNet to deserialise only the two fields we need from the
    /// manifest. Avoids pulling the full BlueprintMetaRootDto deserialisation pipeline (which
    /// lives in the engine package as <c>internal</c>) into the build task.
    /// </summary>
    private sealed class ManifestShape
    {
        public string? BlueprintId { get; set; }
        public string? Description { get; set; }
    }
}
