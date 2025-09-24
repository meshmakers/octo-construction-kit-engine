using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
///     Implements a compiler service for the construction kit.
/// </summary>
public class CompilerService : ICompilerService
{
    private readonly ICkCacheService _ckCacheService;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkValidationService _ckValidationService;
    private readonly ILogger<CompilerService> _logger;

    /// <summary>
    ///     Creates a new instance of the <see cref="CompilerService" /> class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="ckSerializer"></param>
    /// <param name="ckValidationService"></param>
    /// <param name="ckCacheService"></param>
    public CompilerService(ILogger<CompilerService> logger, ICkSerializer ckSerializer, ICkValidationService ckValidationService,
        ICkCacheService ckCacheService)
    {
        _logger = logger;
        _ckSerializer = ckSerializer;
        _ckValidationService = ckValidationService;
        _ckCacheService = ckCacheService;
    }

    /// <inheritdoc />
    public async Task CreateNewAsync(string rootPath)
    {
        ArgumentValidation.ValidateDirectoryPath(nameof(rootPath), rootPath);
        
        // Ensure that the paths are normalized (We do not want to mix separators because it leads to issues)
        rootPath = MmPath.NormalizePath(rootPath);
        
        var operationResult = new OperationResult();

        if (Directory.Exists(rootPath) && Directory.EnumerateFileSystemEntries(rootPath).Any())
        {
            operationResult.AddMessage(MessageCodes.DirectoryMustBeEmpty(rootPath));
            throw CompilerException.DirectoryMustBeEmpty(rootPath, operationResult);
        }

        var typesDirectory = Path.Combine(rootPath, CompilerStatics.TypesDirectoryName);
        var attributesDirectory = Path.Combine(rootPath, CompilerStatics.AttributesDirectoryName);
        var recordsDirectory = Path.Combine(rootPath, CompilerStatics.RecordsDirectoryName);
        var associationsDirectory = Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName);
        var enumsDirectory = Path.Combine(rootPath, CompilerStatics.EnumsDirectoryName);
        Directory.CreateDirectory(recordsDirectory);
        Directory.CreateDirectory(attributesDirectory);
        Directory.CreateDirectory(associationsDirectory);
        Directory.CreateDirectory(typesDirectory);
        Directory.CreateDirectory(enumsDirectory);

        var modelDto = new CkMetaRootDto
        {
            ModelId = "Sample1",
            Dependencies = [new("System", "[1.0,)")]
        };
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(Path.Combine(rootPath, CompilerStatics.MetadataFile));
#else
        await using var streamWriter = new StreamWriter(Path.Combine(rootPath, CompilerStatics.MetadataFile));
#endif
        await _ckSerializer.SerializeAsync(streamWriter, modelDto).ConfigureAwait(false);


        var ckTypeDto = new CkTypeDto
        {
            TypeId = "SampleType1",
            DerivedFromCkTypeId = "${System}/Entity",
            Attributes =
            [
                new() { CkAttributeId = "${thisModel}/SampleAttribute1", AttributeName = "MyAttribute" },
                new() { CkAttributeId = "${thisModel}/SampleAttribute2", AttributeName = "MyRecord" },
                new() { CkAttributeId = "${thisModel}/SampleAttribute3", AttributeName = "MyEnum" }
            ],
            Associations = [new() { CkRoleId = "${thisModel}/Testing", TargetCkTypeId = "${System}/Entity" }]
        };
#if NETSTANDARD2_0
        using var streamWriterEntity = new StreamWriter(Path.Combine(typesDirectory, CompilerStatics.Sample1Entity));
#else
        await using var streamWriterEntity = new StreamWriter(Path.Combine(typesDirectory, CompilerStatics.Sample1Entity));
#endif
        await _ckSerializer.SerializeAsync(streamWriterEntity, new CkElementsRootDto { Types = [ckTypeDto] })
            .ConfigureAwait(false);

        var ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute1",
            ValueType = AttributeValueTypesDto.String
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute1, ckAttributeDto).ConfigureAwait(false);
        ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute2",
            ValueType = AttributeValueTypesDto.Record,
            ValueCkRecordId = "${thisModel}/SampleRecord"
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute2, ckAttributeDto).ConfigureAwait(false);
        ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute3",
            ValueType = AttributeValueTypesDto.Enum,
            ValueCkEnumId = "${thisModel}/SampleEnum"
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute3, ckAttributeDto).ConfigureAwait(false);

        // Write Record
        var ckRecordDto = new CkRecordDto
        {
            RecordId = "SampleRecord",
            Attributes =
                [new() { CkAttributeId = "${thisModel}/SampleAttribute1", AttributeName = "MyAttribute" }]
        };
#if NETSTANDARD2_0
        using var streamWriterRecord = new StreamWriter(Path.Combine(recordsDirectory, CompilerStatics.Sample1Record));
#else
        await using var streamWriterRecord = new StreamWriter(Path.Combine(recordsDirectory, CompilerStatics.Sample1Record));
#endif
        await _ckSerializer.SerializeAsync(streamWriterRecord, new CkElementsRootDto { Records = [ckRecordDto] })
            .ConfigureAwait(false);

        // Write Enum
        var ckEnumDto = new CkEnumDto
        {
            EnumId = "SampleEnum",
            Values =
                new List<CkEnumValueDto>
                {
                    new() { Key = 0, Name = "Name0" },
                    new() { Key = 1, Name = "Name1" },
                    new() { Key = 2, Name = "Name2" }
                }
        };
#if NETSTANDARD2_0
        using var streamWriterEnum = new StreamWriter(Path.Combine(enumsDirectory, CompilerStatics.Sample1Enum));
#else
        await using var streamWriterEnum = new StreamWriter(Path.Combine(enumsDirectory, CompilerStatics.Sample1Enum));
#endif
        await _ckSerializer.SerializeAsync(streamWriterEnum, new CkElementsRootDto { Enums = [ckEnumDto] })
            .ConfigureAwait(false);

        // Write Association
        var ckAssociationRoleDto = new CkAssociationRoleDto
        {
            AssociationRoleId = "Testing",
            InboundName = "Tests",
            OutboundName = "TestedBy",
            InboundMultiplicity = MultiplicitiesDto.N,
            OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne
        };

#if NETSTANDARD2_0
        using var streamWriterAssociations =
            new StreamWriter(Path.Combine(associationsDirectory, CompilerStatics.Sample1Association));
#else
        await using var streamWriterAssociations =
            new StreamWriter(Path.Combine(associationsDirectory, CompilerStatics.Sample1Association));
#endif
        await _ckSerializer.SerializeAsync(streamWriterAssociations,
            new CkElementsRootDto { AssociationRoles = [ckAssociationRoleDto] }).ConfigureAwait(false);

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }
    }

    /// <inheritdoc />
    public async Task<CompileResult> CompileAsync(string rootPath, string outputPath, string? createCacheFilePath)
    {
        var operationResult = new OperationResult();
        return await CompileAsync(rootPath, outputPath, createCacheFilePath, operationResult).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CompileResult> CompileAsync(string rootPath, string outputPath, string? createCacheFilePath, OperationResult operationResult)
    {
        ArgumentValidation.ValidateDirectoryPath(nameof(rootPath), rootPath);
        ArgumentValidation.ValidateDirectoryPath(nameof(outputPath), outputPath);

        // Ensure that the paths are normalized (We do not want to mix separators because it leads to issues)
        outputPath = MmPath.NormalizePath(outputPath);
        rootPath = MmPath.NormalizePath(rootPath);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (!string.IsNullOrWhiteSpace(createCacheFilePath) && createCacheFilePath != null)
        {
            createCacheFilePath = MmPath.NormalizePath(createCacheFilePath);
        }
        
        var originFileResolver = new OriginFileResolver(rootPath);

        if (!Directory.Exists(rootPath))
        {
            operationResult.AddMessage(MessageCodes.DirectoryDoesNotExist(rootPath));
            operationResult.WriteMessagesToLogger(_logger);
            throw CompilerException.DirectoryDoesNotExist(rootPath, operationResult);
        }

        var typesDirectory = Path.Combine(rootPath, CompilerStatics.TypesDirectoryName);
        var attributesDirectory = Path.Combine(rootPath, CompilerStatics.AttributesDirectoryName);
        var associationsDirectory = Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName);
        var recordsDirectory = Path.Combine(rootPath, CompilerStatics.RecordsDirectoryName);
        var enumsDirectory = Path.Combine(rootPath, CompilerStatics.EnumsDirectoryName);

        var ckMetaDto = await GetCkMetaRootDtoAsync(rootPath, originFileResolver, operationResult).ConfigureAwait(false);

        var types = new Dictionary<CkTypeId, CkCompiledTypeDto>();
        if (Directory.Exists(typesDirectory))
        {
            foreach (var typeFile in Directory.EnumerateFiles(typesDirectory, "*.yaml"))
            {
                try
                {
#if NETSTANDARD2_0
                    using var streamType = File.OpenRead(typeFile);
#else
                    await using var streamType = File.OpenRead(typeFile);
#endif
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamType, typeFile, operationResult)
                        .ConfigureAwait(false);
                    if (elementsRootDto.Types != null)
                    {
                        foreach (var ckTypeDto in elementsRootDto.Types)
                        {
                            originFileResolver.Add(new CkId<CkTypeId>(ckMetaDto.ModelId, ckTypeDto.TypeId), typeFile);
                            
                            var ckCompiledTypeDto = new CkCompiledTypeDto
                            {
                                TypeId = ckTypeDto.TypeId,
                                Description = ckTypeDto.Description,
                                DerivedFromCkTypeId = ckTypeDto.DerivedFromCkTypeId,
                                Associations = ckTypeDto.Associations,
                                Attributes = ckTypeDto.Attributes,
                                Indexes = ckTypeDto.Indexes,
                                IsAbstract = ckTypeDto.IsAbstract,
                                IsFinal = ckTypeDto.IsFinal,
                                EnableChangeStreamPreAndPostImages = ckTypeDto.EnableChangeStreamPreAndPostImages
                            };

                            if (types.ContainsKey(ckCompiledTypeDto.TypeId))
                            {
                                operationResult.AddMessage(MessageCodes.TypeIdNotUnique(typeFile, ckCompiledTypeDto.TypeId));
                                operationResult.WriteMessagesToLogger(_logger);
                                throw new CompilerException(operationResult);
                            }
                            types.Add(ckCompiledTypeDto.TypeId, ckCompiledTypeDto);
                        }
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    if (operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        throw CompilerException.ModelParseFailed(typeFile, e, operationResult);
                    }
                }
            }
        }

        var records = new List<CkRecordDto>();
        if (Directory.Exists(recordsDirectory))
        {
            foreach (var recordFile in Directory.EnumerateFiles(recordsDirectory, "*.yaml"))
            {
                try
                {
#if NETSTANDARD2_0
                    using var streamRecord = File.OpenRead(recordFile);
#else
                    await using var streamRecord = File.OpenRead(recordFile);
#endif
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamRecord, recordFile, operationResult)
                        .ConfigureAwait(false);
                    if (elementsRootDto.Records != null)
                    {
                        foreach (var ckRecordDto in elementsRootDto.Records)
                        {
                            originFileResolver.Add(new CkId<CkRecordId>(ckMetaDto.ModelId, ckRecordDto.RecordId), recordFile);
                        }
                        
                        records.AddRange(elementsRootDto.Records);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    if (operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        throw CompilerException.ModelParseFailed(recordFile, e, operationResult);
                    }
                }
            }
        }

        var enums = new List<CkEnumDto>();
        if (Directory.Exists(enumsDirectory))
        {
            foreach (var enumFile in Directory.EnumerateFiles(enumsDirectory, "*.yaml"))
            {
                try
                {
#if NETSTANDARD2_0
                    using var streamEnum = File.OpenRead(enumFile);
#else
                    await using var streamEnum = File.OpenRead(enumFile);
#endif
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamEnum, enumFile, operationResult)
                        .ConfigureAwait(false);
                    if (elementsRootDto.Enums != null)
                    {
                        foreach (var ckEnumDto in elementsRootDto.Enums)
                        {
                            originFileResolver.Add(new CkId<CkEnumId>(ckMetaDto.ModelId, ckEnumDto.EnumId), enumFile);
                        }
                        
                        enums.AddRange(elementsRootDto.Enums);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    if (operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        throw CompilerException.ModelParseFailed(enumFile, e, operationResult);
                    }
                }
            }
        }

        var attributes = new List<CkAttributeDto>();
        if (Directory.Exists(attributesDirectory))
        {
            foreach (var attributeFile in Directory.EnumerateFiles(attributesDirectory, "*.yaml"))
            {
                try
                {
#if NETSTANDARD2_0
                    using var streamAttribute = File.OpenRead(attributeFile);
#else
                    await using var streamAttribute = File.OpenRead(attributeFile);
#endif
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAttribute, attributeFile, operationResult)
                        .ConfigureAwait(false);
                    if (elementsRootDto.Attributes != null)
                    {
                        foreach (var ckAttributeDto in elementsRootDto.Attributes)
                        {
                            originFileResolver.Add(new CkId<CkAttributeId>(ckMetaDto.ModelId, ckAttributeDto.AttributeId), attributeFile);
                        }
                        
                        attributes.AddRange(elementsRootDto.Attributes);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    if (operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        throw CompilerException.ModelParseFailed(attributeFile, e, operationResult);
                    }
                }
            }
        }

        var associationRoles = new List<CkAssociationRoleDto>();
        if (Directory.Exists(associationsDirectory))
        {
            foreach (var associationFile in Directory.EnumerateFiles(associationsDirectory, "*.yaml"))
            {
                try
                {
#if NETSTANDARD2_0
                    using var streamAssociation = File.OpenRead(associationFile);
#else
                    await using var streamAssociation = File.OpenRead(associationFile);
#endif
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAssociation, associationFile, operationResult)
                        .ConfigureAwait(false);
                    if (elementsRootDto.AssociationRoles != null)
                    {
                        foreach (var ckAssociationRoleDto in elementsRootDto.AssociationRoles)
                        {
                            originFileResolver.Add(new CkId<CkAssociationRoleId>(ckMetaDto.ModelId,
                                ckAssociationRoleDto.AssociationRoleId), associationFile);
                        }
                        
                        associationRoles.AddRange(elementsRootDto.AssociationRoles);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    if (operationResult.HasErrors || operationResult.HasFatalErrors)
                    {
                        throw CompilerException.ModelParseFailed(associationFile, e, operationResult);
                    }
                }
            }
        }

        var compileCandidate = new CkModelCompileCandidate
        {
            ModelId = ckMetaDto.ModelId,
            DependencyRanges = ckMetaDto.Dependencies?.OrderBy(x=> x.ModelId).ToList(),
            Description = ckMetaDto.Description,
            Types = types.Values.OrderBy(x=> x.TypeId).ToList(),
            Attributes = attributes.OrderBy(x=> x.AttributeId).ToList(),
            AssociationRoles = associationRoles.OrderBy(x=> x.AssociationRoleId).ToList(),
            Records = records.OrderBy(x=> x.RecordId).ToList(),
            Enums = enums.OrderBy(x=> x.EnumId).ToList(),
        };

        var (ckModelGraph, compiledModelRoot) = await _ckValidationService.ValidateAsync(compileCandidate, originFileResolver, operationResult).ConfigureAwait(false);

        foreach (var keyValuePair in ckModelGraph.Types.Where(s => s.Key.ModelId == ckMetaDto.ModelId
                                                                   && s.Value.IsCollectionRoot))
        {
            types[keyValuePair.Key.Key].IsCollectionRoot = keyValuePair.Value.IsCollectionRoot;
        }

        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }

        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        var compiledModelFile = $"ck-{ckMetaDto.ModelId.SemanticVersionedFullName.ToLower()}.yaml";
        var compiledModelFilePath = Path.Combine(outputPath, compiledModelFile);
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(compiledModelFilePath);
#else
        await using var streamWriter = new StreamWriter(compiledModelFilePath);
#endif
        await _ckSerializer.SerializeAsync(streamWriter, compiledModelRoot).ConfigureAwait(false);

#if NETSTANDARD2_0
        if (!string.IsNullOrWhiteSpace(createCacheFilePath) && createCacheFilePath != null)
#else
        if (!string.IsNullOrWhiteSpace(createCacheFilePath))
#endif
        {
            var compiledModelCacheFilePath = await CreateCacheFileAsync(ckModelGraph, compileCandidate.ModelId, createCacheFilePath).ConfigureAwait(false);
            return new CompileResult(compiledModelFilePath, compiledModelCacheFilePath);
        }

        return new CompileResult(compiledModelFilePath);
    }

    private async Task<CkMetaRootDto> GetCkMetaRootDtoAsync(string rootPath, OriginFileResolver originFileResolver, OperationResult operationResult)
    {
        var modelPath = Path.Combine(rootPath, CompilerStatics.MetadataFile);
        if (!File.Exists(modelPath))
        {
            operationResult.AddMessage(MessageCodes.FileDoesNotExist(modelPath));
            operationResult.WriteMessagesToLogger(_logger);
            throw CompilerException.FileDoesNotExist(modelPath, operationResult);
        }

#if NETSTANDARD2_0
        using var stream = File.OpenRead(modelPath);
#else
        using var stream = File.OpenRead(modelPath);
#endif
        var ckMetaDto = await _ckSerializer.DeserializeMetaAsync(stream, modelPath, operationResult).ConfigureAwait(false);
        originFileResolver.Add(ckMetaDto.ModelId, modelPath);
        return ckMetaDto;
    }

    private async Task WriteAttributeAsync(string attributesDirectory, string attributeFileName, CkAttributeDto ckAttributeDto)
    {
#if NETSTANDARD2_0
        using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, attributeFileName));
#else
        await using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, attributeFileName));
#endif
        await _ckSerializer.SerializeAsync(streamWriterAttribute,
            new CkElementsRootDto { Attributes = [ckAttributeDto] }).ConfigureAwait(false);
    }

    private async Task<string> CreateCacheFileAsync(ICkModelGraph ckModelGraph, CkModelId ckModelId, string outputPath)
    {
        var tempTenantId = Guid.NewGuid().ToString();
        _ckCacheService.CreateTenant(tempTenantId);
        _ckCacheService.LoadCkModelGraph(tempTenantId, ckModelGraph);
        
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        var compiledModelCacheFilePath = GetCompiledModelCacheFilePath(ckModelId, outputPath);
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(compiledModelCacheFilePath);
#else
        await using var streamWriter = new StreamWriter(compiledModelCacheFilePath);
#endif
        await _ckCacheService.SaveCacheAsync(tempTenantId, streamWriter.BaseStream).ConfigureAwait(false);

        return compiledModelCacheFilePath;
    }

    private string GetCompiledModelCacheFilePath(CkModelId ckModelId, string outputPath)
    {
        var compiledModelCacheFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.cache.json";
        var compiledModelCacheFilePath = Path.Combine(outputPath, compiledModelCacheFile);
        return compiledModelCacheFilePath;
    }
}