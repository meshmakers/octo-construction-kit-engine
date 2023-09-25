using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

[Generator]
public class CkSourceGenerator : IIncrementalGenerator
{
    private readonly ServiceProvider _serviceProvider;

    public CkSourceGenerator()
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
        var globalOptions = context.AnalyzerConfigOptionsProvider.Select(GlobalOptions.Select);

        var ckModelCandidates = context.AdditionalTextsProvider
            .Where(static x =>
            {
                var fileName = Path.GetFileName(x.Path).ToLower();
                return fileName.StartsWith("ck-") && (fileName.EndsWith(".cache.json") || fileName.EndsWith(".yaml"));
            })
            .Select(static (f, _) => new AdditionalTextWithHash(f, Guid.NewGuid()));
        
        var monitor = ckModelCandidates.Collect().SelectMany(static (x, _) => GroupCkModelFiles.Group(x));
        
        var inputs = monitor
            .Combine(globalOptions)
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (x, _) => FileOptions.Select(
                file: x.Left.Left,
                options: x.Right,
                globalOptions: x.Left.Right
            ))
            .Where(static x => x.IsValid);
        
        context.RegisterSourceOutput(inputs, GenerateCode);
    }

    private void GenerateCode(SourceProductionContext context, FileOptions fileOptions)
    {
        var ckCacheService = _serviceProvider.GetRequiredService<ICkCacheService>();
        var ckSerializer = _serviceProvider.GetRequiredService<ICkYamlSerializer>();
        
        var tenantId = fileOptions.GroupedFile.MainFile.Hash.ToString();
        
        ckCacheService.CreateTenant(tenantId);
        fileOptions.GroupedFile.CacheFile.Deconstruct(out var cacheFile, out _);
        var sourceText = cacheFile.GetText();
        if (sourceText == null)
        {
            
            var error = Diagnostic.Create(DiagnosticsDescriptors.EmptyFile,
                null,
                fileOptions.GroupedFile.CacheFile.File.Path);
            context.ReportDiagnostic(error);
            return;
        }
        ckCacheService.RestoreCache(tenantId, sourceText.ToString());
        
        fileOptions.GroupedFile.MainFile.Deconstruct(out var mainFile, out _);
        sourceText = mainFile.GetText();
        if (sourceText == null)
        {
            var error = Diagnostic.Create(DiagnosticsDescriptors.EmptyFile,
                null,
                fileOptions.GroupedFile.MainFile.File.Path);
            context.ReportDiagnostic(error);
            return;
        }
        
        var operationResult = new OperationResult();
        var ckCompiledModelRoot = ckSerializer.DeserializeCompiledModelRoot(sourceText.ToString(), mainFile.Path, operationResult);
        if (operationResult.Messages.Any())
        {
            ReportOperationResults(context, operationResult);
            return;
        }

        var ns = $"{fileOptions.LocalNamespace}.Generated.{ckCompiledModelRoot.ModelId.ModelId}.v{ckCompiledModelRoot.ModelId.ModelVersion.Major.ToString()}";
        
        if (ckCompiledModelRoot.Types != null)
        {
            foreach (var ckTypeDto in ckCompiledModelRoot.Types)
            {
                if (ckCompiledModelRoot.ModelId.ModelId == "System" && ckTypeDto.TypeId.TypeId == "Entity")
                {
                    continue;
                }
                
                var code = CkTypeCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckTypeDto, tenantId, ckCacheService);
                if (!String.IsNullOrWhiteSpace(code))
                {
                    context.AddSource($"{ns}.{ckTypeDto.TypeId.TypeId}.g.cs", code);
                }
            }
        }
        
        if (ckCompiledModelRoot.Records != null)
        {
            foreach (var ckRecordDto in ckCompiledModelRoot.Records)
            {
                var code = CkRecordCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckRecordDto, tenantId, ckCacheService);
                if (!String.IsNullOrWhiteSpace(code))
                {
                    context.AddSource($"{ns}.Record.{ckRecordDto.RecordId.RecordId}.g.cs", code);
                }
            }
        }
                
        if (ckCompiledModelRoot.Enums != null)
        {
            foreach (var ckEnumDto in ckCompiledModelRoot.Enums)
            {
                var code = CkEnumCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckEnumDto, tenantId, ckCacheService);
                if (!String.IsNullOrWhiteSpace(code))
                {
                    context.AddSource($"{ns}.Enum.{ckEnumDto.EnumId.EnumId}.g.cs", code);
                }
            }
        }
        
        var generatedCode = CkIdsCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Types, 
            ckCompiledModelRoot.Attributes, ckCompiledModelRoot.AssociationRoles);
        context.AddSource($"{ns}.Common.CkIds.g.cs", generatedCode);

        if (fileOptions.GenerateCkModelServiceClass)
        {
            generatedCode = CkEmbeddedModelGenerator.Instance.Generate(ns, fileOptions.LocalNamespace, ckCompiledModelRoot.ModelId);
            context.AddSource($"{ns}.Common.Service.g.cs", generatedCode);
            
            generatedCode = CkEmbeddedModelDiGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId);
            context.AddSource($"{ns}.Common.ServiceDi.g.cs", generatedCode);
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