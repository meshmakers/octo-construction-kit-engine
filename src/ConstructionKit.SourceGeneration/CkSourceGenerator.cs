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
    private SourceProductionContext? _currentContext;

    public CkSourceGenerator()
    {
        try
        {
            var services = new ServiceCollection();

            // Configure logging for source generator environment
            // We need to add logging services because the Engine services depend on ILogger<T>
            // but we use a NullLoggerProvider so it doesn't try to write to console/files
            services.AddLogging(loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.AddProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerProvider.Instance);
                loggingBuilder.SetMinimumLevel(LogLevel.Trace);
            });

            services.AddConstructionKit();
            _serviceProvider = services.BuildServiceProvider();
        }
        catch (Exception e)
        {
            LogDiagnostic(DiagnosticSeverity.Info,
                $"Cannot create instance of type {nameof(CkSourceGenerator)}: {e.Message}");
            throw;
        }
    }

    /// <summary>
    /// Logs diagnostic information during source generation.
    /// This is the proper way to log in source generators instead of using ILogger.
    /// </summary>
    private void LogDiagnostic(DiagnosticSeverity severity, string message, Location? location = null)
    {
        if (_currentContext == null) return;

        var descriptor = severity switch
        {
            DiagnosticSeverity.Error => DiagnosticsDescriptors.GeneratorError,
            DiagnosticSeverity.Warning => DiagnosticsDescriptors.GeneratorWarning,
            _ => DiagnosticsDescriptors.GeneratorInfo
        };

        _currentContext.Value.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var globalOptions = context.AnalyzerConfigOptionsProvider.Select(GlobalOptions.Select);

        var ckModelCandidates = context.AdditionalTextsProvider
            .Combine(globalOptions)
            .Where(static x =>
            {
                var f = x.Left;
                var options = x.Right;
                if (!options.IsValid) // if not valid -> the project has not loaded props file with CompilerVisibleProperty
                {
                    return false;
                }

                var fileName = Path.GetFileName(f.Path).ToLower();
                return fileName.StartsWith("ck-") && (fileName.EndsWith(".cache.json") || fileName.EndsWith(".yaml"));
            })
            .Select(static (f, _) =>
            {
                var checksum = f.Left.GetText()?.GetChecksum();
                return new AdditionalTextWithHash(f.Left,
                    checksum == null ? null : BitConverter.ToString(checksum.Value.ToArray()));
            });

        var monitor = ckModelCandidates.Collect().SelectMany(static (x, _) => GroupCkModelFiles.Group(x));

        var inputs = monitor
            .Combine(globalOptions)
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (x, _) => FileOptions.Select(
                x.Left.Left,
                x.Right,
                x.Left.Right
            ))
            .Where(static x => x.IsValid);

        context.RegisterSourceOutput(inputs, GenerateCode);
    }

    private void GenerateCode(SourceProductionContext context, FileOptions fileOptions)
    {
        // Set the current context for diagnostic logging
        _currentContext = context;

        try
        {
            LogDiagnostic(DiagnosticSeverity.Info, 
                $"Starting code generation for model: {fileOptions.GroupedFile.MainFile.File.Path}");

            var ckCacheService = _serviceProvider.GetRequiredService<ICkCacheService>();
            var ckSerializer = _serviceProvider.GetRequiredService<ICkYamlSerializer>();

            var tenantId = fileOptions.GroupedFile.MainFile.File.Path;

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

            LogDiagnostic(DiagnosticSeverity.Info, "Restoring cache from cache file");
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

            LogDiagnostic(DiagnosticSeverity.Info, "Deserializing compiled model root");
            var operationResult = new OperationResult();
            var ckCompiledModelRoot =
                ckSerializer.DeserializeCompiledModelRoot(sourceText.ToString(), mainFile.Path, operationResult);
            if (operationResult.Messages.Any())
            {
                ReportOperationResults(context, operationResult);
                return;
            }

            var ns =
                $"{fileOptions.LocalNamespace}.Generated.{ckCompiledModelRoot.ModelId.Name}.v{ckCompiledModelRoot.ModelId.Version.Major.ToString()}";

            LogDiagnostic(DiagnosticSeverity.Info, 
                $"Generating code for model {ckCompiledModelRoot.ModelId.Name} v{ckCompiledModelRoot.ModelId.Version} in namespace {ns}");

            if (ckCompiledModelRoot.Types != null)
            {
                LogDiagnostic(DiagnosticSeverity.Info, $"Generating {ckCompiledModelRoot.Types.Count()} type(s)");
                foreach (var ckTypeDto in ckCompiledModelRoot.Types)
                {
                    if (ckCompiledModelRoot.ModelId.Name == "System" && ckTypeDto.TypeId.Name == "Entity")
                    {
                        continue;
                    }

                    var code = CkTypeCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckTypeDto, tenantId,
                        ckCacheService);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        var fileName = $"{ns}.{ckTypeDto.TypeId.Name}.{ckTypeDto.TypeId.Version}.g.cs";
                        context.AddSource(fileName, code);
                        LogDiagnostic(DiagnosticSeverity.Info, $"Generated type: {ckTypeDto.TypeId.Name}");
                    }
                }
            }

            if (ckCompiledModelRoot.Records != null)
            {
                LogDiagnostic(DiagnosticSeverity.Info, $"Generating {ckCompiledModelRoot.Records.Count()} record(s)");
                foreach (var ckRecordDto in ckCompiledModelRoot.Records)
                {
                    var code = CkRecordCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId, ckRecordDto,
                        tenantId, ckCacheService);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        var fileName = $"{ns}.Record.{ckRecordDto.RecordId.Name}.{ckRecordDto.RecordId.Version}.g.cs";
                        context.AddSource(fileName, code);
                        LogDiagnostic(DiagnosticSeverity.Info, $"Generated record: {ckRecordDto.RecordId.Name}");
                    }
                }
            }

            if (ckCompiledModelRoot.Enums != null)
            {
                LogDiagnostic(DiagnosticSeverity.Info, $"Generating {ckCompiledModelRoot.Enums.Count()} enum(s)");
                foreach (var ckEnumDto in ckCompiledModelRoot.Enums)
                {
                    var code = CkEnumCodeGenerator.Instance.Generate(ns, ckEnumDto);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        var fileName = $"{ns}.Enum.{ckEnumDto.EnumId.Name}.{ckEnumDto.EnumId.Version}.g.cs";
                        context.AddSource(fileName, code);
                        LogDiagnostic(DiagnosticSeverity.Info, $"Generated enum: {ckEnumDto.EnumId.Name}");
                    }
                }
            }

            LogDiagnostic(DiagnosticSeverity.Info, "Generating common infrastructure files");
            
            var generatedCode = CkIdsCodeGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId,
                ckCompiledModelRoot.Types,
                ckCompiledModelRoot.Attributes, ckCompiledModelRoot.AssociationRoles);
            context.AddSource($"{ns}.Common.CkIds.g.cs", generatedCode);

            generatedCode = CkEmbeddedModelGenerator.Instance.Generate(ns, fileOptions.LocalNamespace,
                ckCompiledModelRoot.ModelId, ckCompiledModelRoot.Description);
            context.AddSource($"{ns}.Common.Service.g.cs", generatedCode);

            generatedCode =
                CkEmbeddedModelDiGenerator.Instance.Generate(ns, ckCompiledModelRoot.ModelId,
                    ckCompiledModelRoot.Types != null);
            context.AddSource($"{ns}.Common.ServiceDi.g.cs", generatedCode);

            generatedCode = CkClassMapGenerator.Instance.Generate(ns, ckCompiledModelRoot.Types,
                ckCompiledModelRoot.Records, ckCompiledModelRoot.ModelId);
            context.AddSource($"{ns}.Common.CkTypeMap.g.cs", generatedCode);

            LogDiagnostic(DiagnosticSeverity.Info, 
                $"Code generation completed successfully for model {ckCompiledModelRoot.ModelId.Name}");
        }
        catch (Exception ex)
        {
            LogDiagnostic(DiagnosticSeverity.Error, 
                $"Code generation failed with exception: {ex.GetType().Name}: {ex.Message}\nStack trace: {ex.StackTrace}");
        }
        finally
        {
            _currentContext = null;
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