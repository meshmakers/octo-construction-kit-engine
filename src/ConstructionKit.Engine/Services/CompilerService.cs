using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Implements a compiler service for the construction kit.
/// </summary>
public class CompilerService : ICompilerService
{
    private readonly ILogger<CompilerService> _logger;
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkValidationService _ckValidationService;
    private readonly ICkCacheService _ckCacheService;

    /// <summary>
    /// Creates a new instance of the <see cref="CompilerService"/> class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="ckSerializer"></param>
    /// <param name="ckValidationService"></param>
    /// <param name="ckCacheService"></param>
    public CompilerService(ILogger<CompilerService> logger, ICkSerializer ckSerializer, ICkValidationService ckValidationService, ICkCacheService ckCacheService)
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

        OperationResult operationResult = new OperationResult();

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
            Dependencies = new List<CkModelId> { new("System") }
        };
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(Path.Combine(rootPath, CompilerStatics.MetadataFile));
#else
        await using var streamWriter = new StreamWriter(Path.Combine(rootPath, CompilerStatics.MetadataFile));
#endif
        await _ckSerializer.SerializeAsync(streamWriter, modelDto);


        var ckTypeDto = new CkTypeDto
        {
            TypeId = "SampleType1",
            DerivedFromCkTypeId = "System/Entity",
            Attributes =
                new List<CkTypeAttributeDto>
                {
                    new() { CkAttributeId = "Sample1/SampleAttribute1", AttributeName = "MyAttribute" },
                    new() { CkAttributeId = "Sample1/SampleAttribute2", AttributeName = "MyRecord" },
                    new() { CkAttributeId = "Sample1/SampleAttribute3", AttributeName = "MyEnum" }
                },
            Associations = new List<CkTypeAssociationDto> { new() { CkRoleId = "Sample1/Testing", TargetCkTypeId = "System/Entity" } }
        };
#if NETSTANDARD2_0
        using var streamWriterEntity = new StreamWriter(Path.Combine(typesDirectory, CompilerStatics.Sample1Entity));
#else
        await using var streamWriterEntity = new StreamWriter(Path.Combine(typesDirectory, CompilerStatics.Sample1Entity));
#endif
        await _ckSerializer.SerializeAsync(streamWriterEntity, new CkElementsRootDto { Types = new List<CkTypeDto> { ckTypeDto } });

        var ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute1",
            ValueType = AttributeValueTypesDto.String
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute1, ckAttributeDto);        
        ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute2",
            ValueType = AttributeValueTypesDto.Record,
            ValueCkRecordId = "Sample1/SampleRecord"
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute2, ckAttributeDto);
        ckAttributeDto = new CkAttributeDto
        {
            AttributeId = "SampleAttribute3",
            ValueType = AttributeValueTypesDto.Enum,
            ValueCkEnumId = "Sample1/SampleEnum"
        };
        await WriteAttributeAsync(attributesDirectory, CompilerStatics.Sample1Attribute3, ckAttributeDto);
        
        // Write Record
        var ckRecordDto = new CkRecordDto
        {
            RecordId = "SampleRecord",
            Attributes =
                new List<CkTypeAttributeDto> { new() { CkAttributeId = "Sample1/SampleAttribute1", AttributeName = "MyAttribute" } },
        };
#if NETSTANDARD2_0
        using var streamWriterRecord = new StreamWriter(Path.Combine(recordsDirectory, CompilerStatics.Sample1Record));
#else
        await using var streamWriterRecord = new StreamWriter(Path.Combine(recordsDirectory, CompilerStatics.Sample1Record));
#endif
        await _ckSerializer.SerializeAsync(streamWriterRecord, new CkElementsRootDto { Records = new List<CkRecordDto> { ckRecordDto } });
        
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
                },
        };
#if NETSTANDARD2_0
        using var streamWriterEnum = new StreamWriter(Path.Combine(enumsDirectory, CompilerStatics.Sample1Enum));
#else
        await using var streamWriterEnum = new StreamWriter(Path.Combine(enumsDirectory, CompilerStatics.Sample1Enum));
#endif
        await _ckSerializer.SerializeAsync(streamWriterEnum, new CkElementsRootDto { Enums = new List<CkEnumDto> { ckEnumDto } });

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
            new CkElementsRootDto { AssociationRoles = new List<CkAssociationRoleDto> { ckAssociationRoleDto } });

        if (operationResult.HasErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }
    }

    private async Task WriteAttributeAsync(string attributesDirectory, string attributeFileName, CkAttributeDto ckAttributeDto)
    {
#if NETSTANDARD2_0
        using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, attributeFileName));
#else
        await using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, attributeFileName));
#endif
        await _ckSerializer.SerializeAsync(streamWriterAttribute,
            new CkElementsRootDto { Attributes = new List<CkAttributeDto> { ckAttributeDto } });
    }

    /// <inheritdoc />
    public async Task<string> CompileAsync(string rootPath, bool createCacheFile)
    {
        ArgumentValidation.ValidateDirectoryPath(nameof(rootPath), rootPath);

        OperationResult operationResult = new OperationResult();

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
        await using var stream = File.OpenRead(modelPath);
#endif
        var ckMetaDto = await _ckSerializer.DeserializeMetaAsync(stream, modelPath, operationResult);

        var types = new List<CkTypeDto>();
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
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamType, typeFile, operationResult);
                    if (elementsRootDto.Types != null)
                    {
                        types.AddRange(elementsRootDto.Types);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    throw CompilerException.ModelParseFailed(typeFile, e, operationResult);
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
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamRecord, recordFile, operationResult);
                    if (elementsRootDto.Records != null)
                    {
                        records.AddRange(elementsRootDto.Records);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    throw CompilerException.ModelParseFailed(recordFile, e, operationResult);
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
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamEnum, enumFile, operationResult);
                    if (elementsRootDto.Enums != null)
                    {
                        enums.AddRange(elementsRootDto.Enums);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    throw CompilerException.ModelParseFailed(enumFile, e, operationResult);
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
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAttribute, attributeFile, operationResult);
                    if (elementsRootDto.Attributes != null)
                    {
                        attributes.AddRange(elementsRootDto.Attributes);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    throw CompilerException.ModelParseFailed(attributeFile, e, operationResult);
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
                    var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAssociation, associationFile, operationResult);
                    if (elementsRootDto.AssociationRoles != null)
                    {
                        associationRoles.AddRange(elementsRootDto.AssociationRoles);
                    }
                }
                catch (ModelParseException e)
                {
                    operationResult.WriteMessagesToLogger(_logger);
                    throw CompilerException.ModelParseFailed(associationFile, e, operationResult);
                }
            }
        }

        CkCompiledModelRoot compiledModelRoot = new CkCompiledModelRoot
        {
            ModelId = ckMetaDto.ModelId,
            Dependencies = ckMetaDto.Dependencies,
            Types = types,
            Attributes = attributes,
            AssociationRoles = associationRoles,
            Records = records,
            Enums = enums
        };

        var ckModelGraph = await _ckValidationService.ValidateAsync(compiledModelRoot, operationResult);

        if (operationResult.HasErrors)
        {
            operationResult.WriteMessagesToLogger(_logger);
            throw CompilerException.OperationResultWithErrors(operationResult);
        }

        string compiledModelFile = $"ck-{ckMetaDto.ModelId.SemanticVersionedFullName.ToLower()}.yaml";
        var compiledModelFilePath = Path.Combine(rootPath, compiledModelFile);
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(compiledModelFilePath);
#else
        await using var streamWriter = new StreamWriter(compiledModelFilePath);
#endif
        await _ckSerializer.SerializeAsync(streamWriter, compiledModelRoot);

        if (createCacheFile)
        {
            await CreateCacheFileAsync(ckModelGraph, compiledModelRoot.ModelId, rootPath);
        }

        return compiledModelFilePath;
    }

    private async Task CreateCacheFileAsync(CkModelGraph ckModelGraph, CkModelId ckModelId, string rootPath)
    {
        var tempTenantId = Guid.NewGuid().ToString();
        _ckCacheService.CreateTenant(tempTenantId);
        _ckCacheService.LoadCkModelGraph(tempTenantId, ckModelGraph);
        
        string compiledModelCacheFile = $"ck-{ckModelId.SemanticVersionedFullName.ToLower()}.cache.json";
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelCacheFile));
#else
        await using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelCacheFile));
#endif
        await _ckCacheService.SaveCacheAsync(tempTenantId, streamWriter.BaseStream);
    }
}