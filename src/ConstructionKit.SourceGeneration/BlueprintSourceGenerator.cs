using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
/// Incremental source generator that reads <c>blueprints-cache.json</c> files produced by the
/// <c>BlueprintEmbed</c> MSBuild task (surfaced as <c>AdditionalFiles</c>) and emits an
/// <c>IBlueprintEmbeddedSource</c> implementation plus a
/// <c>AddBlueprint{Name}V{Major}(this IServiceCollection)</c> DI extension for every cached
/// blueprint version.
///
/// Mirrors <see cref="CkSourceGenerator" /> for embedded CK models: the heavy lifting (file
/// scanning, schema validation, manifest reading) happens at build time in the MSBuild task; this
/// generator only deserialises the inventory and writes deterministic C# files.
/// </summary>
[Generator]
public class BlueprintSourceGenerator : IIncrementalGenerator
{
    private const string ExpectedCacheFileName = "blueprints-cache.json";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var globalOptions = context.AnalyzerConfigOptionsProvider.Select(GlobalOptions.Select);

        var cacheFiles = context.AdditionalTextsProvider
            .Where(static t =>
                string.Equals(Path.GetFileName(t.Path), ExpectedCacheFileName, StringComparison.OrdinalIgnoreCase));

        var inputs = cacheFiles.Combine(globalOptions);

        context.RegisterSourceOutput(inputs, GenerateForCacheFile);
    }

    private static void GenerateForCacheFile(SourceProductionContext context,
        (AdditionalText Left, GlobalOptions Right) input)
    {
        var (text, globalOptions) = input;
        if (!globalOptions.IsValid)
        {
            // Without the project metadata (RootNamespace / ProjectName) we cannot place generated
            // classes deterministically. Skip silently — the corresponding build/props file isn't
            // applied yet (e.g. the consumer hasn't imported our targets).
            return;
        }

        var sourceText = text.GetText(context.CancellationToken);
        if (sourceText == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.EmptyFile, null, text.Path));
            return;
        }

        var json = sourceText.ToString();

        // Schema-validate the cache before consuming it. The MSBuild task is the producer, so this
        // is normally a no-op — but if a stale cache from an older format ever sneaks in we want a
        // build error pointing at the file, not a NullReferenceException downstream.
        var operationResult = new OperationResult();
        using (var validationStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
        {
            new BlueprintSchemaValidator().ValidateCacheInJson(validationStream, text.Path, operationResult);
        }
        if (operationResult.HasErrors)
        {
            foreach (var m in operationResult.Messages.Where(x => x.MessageLevel == MessageLevel.Error || x.MessageLevel == MessageLevel.FatalError))
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.GeneratorError,
                    null, $"{text.Path}: {m.MessageText}"));
            }
            return;
        }

        BlueprintsCacheDto? cache;
        try
        {
            cache = JsonSerializer.Deserialize<BlueprintsCacheDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.GeneratorError, null,
                $"Failed to deserialise blueprint cache '{text.Path}': {ex.Message}"));
            return;
        }

        if (cache == null || cache.Blueprints.Count == 0)
        {
            return;
        }

        if (cache.Version != "1")
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.GeneratorError, null,
                $"Unsupported blueprint cache version '{cache.Version}' in '{text.Path}'. Expected '1'."));
            return;
        }

        var rootNs = Utilities.SanitizeNamespace(globalOptions.RootNamespace ?? globalOptions.ProjectName);

        foreach (var entry in cache.Blueprints)
        {
            BlueprintId blueprintId;
            try
            {
                blueprintId = new BlueprintId($"{entry.Name}-{entry.Version}");
            }
            catch (Exception ex)
            {
                context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.GeneratorError, null,
                    $"Invalid blueprint identifier '{entry.Name}-{entry.Version}' in '{text.Path}': {ex.Message}"));
                continue;
            }

            // Generated classes live under {RootNamespace}.Generated.Blueprints.{Name}.v{Major}
            // mirroring the CK-model generator's namespace strategy. Inserting the major version in
            // the namespace makes multi-version coexistence painless: both classes can carry the
            // same base name without colliding.
            var generatedNs = $"{rootNs}.Generated.Blueprints.{blueprintId.Name.MakeClassName()}.v{blueprintId.Version.Major}";

            var code = BlueprintEmbeddedSourceGenerator.Generate(generatedNs, entry, blueprintId);
            var fileName = $"{generatedNs}.BlueprintEmbeddedSource.g.cs";
            context.AddSource(fileName, SourceText.From(code, System.Text.Encoding.UTF8));
        }
    }
}
