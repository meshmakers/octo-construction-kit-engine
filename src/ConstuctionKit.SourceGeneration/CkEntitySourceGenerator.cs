using System.Collections.Immutable;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

[Generator]
public class CkEntitySourceGenerator : IIncrementalGenerator
{
    private readonly ServiceProvider _serviceProvider;

    public CkEntitySourceGenerator()
    {
        var services = new ServiceCollection();
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        services.AddConstructionKit();
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {

        var options = context.AnalyzerConfigOptionsProvider;
        
        var ckCacheFiles = context.AdditionalTextsProvider
            .Where(text =>
            {
                var fileName = Path.GetFileName(text.Path);
                return fileName.StartsWith("ck-", StringComparison.OrdinalIgnoreCase)
                       && text.Path.EndsWith(".cache.json", StringComparison.OrdinalIgnoreCase);
            })
            .Select((text, token) => new Tuple<string, string?>(Path.GetFileName(text.Path).ToLower(), text.GetText(token)?.ToString()))
            .Where(tuple => tuple.Item2 is not null)
            .Collect();
        
        var ckCompiledCkModels = context.AdditionalTextsProvider
            .Where(text =>
            {
                var fileName = Path.GetFileName(text.Path);
                return fileName.StartsWith("ck-", StringComparison.OrdinalIgnoreCase)
                       && text.Path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
            })
            .Select((text, token) => new Tuple<string, string?>(Path.GetFileName(text.Path).ToLower(), text.GetText(token)?.ToString()))
            .Where(tuple => tuple.Item2 is not null)
            .Collect();
        
        context.RegisterSourceOutput(ckCacheFiles.Combine(ckCompiledCkModels).Combine(options), GenerateCode);
    }

    private void GenerateCode(SourceProductionContext context, ((ImmutableArray<Tuple<string, string?>> Left, ImmutableArray<Tuple<string, string?>> Right) Left, AnalyzerConfigOptionsProvider Right) args)
    {
        var ckCacheService = _serviceProvider.GetRequiredService<ICkCacheService>();
        var ckSerializer = _serviceProvider.GetRequiredService<ICkYamlSerializer>();

        var (ckCacheFileTuples, ckCompiledModelTuples) = args.Left;
        var options = args.Right;
        
        options.GlobalOptions.TryGetValue("build_property.rootnamespace", out var rootNamespace);
        
        foreach (var ckCacheFile in ckCacheFileTuples)
        {
            var tenantId = ckCacheFile.Item1;
            var cacheJson = ckCacheFile.Item2;
            if (!string.IsNullOrWhiteSpace(cacheJson))
            {
                ckCacheService.CreateTenant(tenantId);
                ckCacheService.RestoreCache(tenantId, cacheJson);
            }
        }

        foreach (var ckCompiledModelRootTuple in ckCompiledModelTuples)
        {
            if (ckCompiledModelRootTuple.Item2 == null)
            {
                continue;
            }
            var operationResult = new OperationResult();
            var ckCompiledModelRoot = ckSerializer.DeserializeCompiledModelRoot(ckCompiledModelRootTuple.Item2, operationResult);
            if (operationResult.Messages.Any())
            {
                ReportOperationResults(context, operationResult);
                continue;
            }

            string tenantId = $"ck-{ckCompiledModelRoot.ModelId.SemanticVersionedFullName.ToLower()}.cache.json";

            if (ckCompiledModelRoot.Types != null)
            {
                foreach (var ckTypeDto in ckCompiledModelRoot.Types)
                {
                    var ns = $"{rootNamespace ?? "Undefined"}.Generated.v{ckTypeDto.TypeId.Version.Major.ToString()}";
                    var code = CkTypeCkTypeCodeGenerator.Instance.Generate(ns, ckTypeDto, tenantId, ckCacheService);
                    if (!String.IsNullOrWhiteSpace(code))
                    {
                        context.AddSource($"{ns}.{ckTypeDto.TypeId.TypeId}.g.cs", code);
                    }
                } 
            }
        }
    }

    private static void ReportOperationResults(SourceProductionContext context, OperationResult operationResult)
    {
        foreach (var message in operationResult.Messages)
        {
            switch (message.MessageLevel)
            {
                case MessageLevel.FatalError:
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                            $"O{message.MessageNumber.ToString().PadLeft(3, '0')}",
                            "Construction Kit read fatal error",
                            message.MessageText, "Construction Kit", DiagnosticSeverity.Error, true),
                        null));
                    break;
                case MessageLevel.Error:
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                            $"O{message.MessageNumber.ToString().PadLeft(3, '0')}",
                            "Construction Kit read error",
                            message.MessageText, "Construction Kit", DiagnosticSeverity.Error, true),
                        null));
                    break;
                case MessageLevel.Warning:
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                            $"O{message.MessageNumber.ToString().PadLeft(3, '0')}",
                            "Construction Kit read warning",
                            message.MessageText, "Construction Kit", DiagnosticSeverity.Warning, true),
                        null));
                    break;
                case MessageLevel.Info:
                    context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                            $"O{message.MessageNumber.ToString().PadLeft(3, '0')}",
                            "Construction Kit read",
                            message.MessageText, "Construction Kit", DiagnosticSeverity.Info, true),
                        null));
                    break;
            }
        }
    }
}