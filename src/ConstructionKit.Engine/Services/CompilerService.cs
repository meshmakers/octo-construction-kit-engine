using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
/// Implements a compiler service for the construction kit.
/// </summary>
public class CompilerService : ICompilerService
{
    private readonly ICkSerializer _ckSerializer;
    private readonly ICkValidationService _ckValidationService;
    private readonly ICkCacheService _ckCacheService;

    /// <summary>
    /// Creates a new instance of the <see cref="CompilerService"/> class.
    /// </summary>
    /// <param name="ckSerializer"></param>
    /// <param name="ckValidationService"></param>
    /// <param name="ckCacheService"></param>
    public CompilerService(ICkSerializer ckSerializer, ICkValidationService ckValidationService, ICkCacheService ckCacheService)
    {
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
        var associationsDirectory = Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName);
        Directory.CreateDirectory(attributesDirectory);
        Directory.CreateDirectory(associationsDirectory);
        Directory.CreateDirectory(typesDirectory);

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
                new List<CkTypeAttributeDto> { new() { CkAttributeId = "Sample1/SampleAttribute", AttributeName = "MyAttribute" } },
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
            AttributeId = "SampleAttribute",
            ValueType = AttributeValueTypesDto.String
        };
#if NETSTANDARD2_0
        using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, CompilerStatics.Sample1Attribute));
#else
        await using var streamWriterAttribute = new StreamWriter(Path.Combine(attributesDirectory, CompilerStatics.Sample1Attribute));
#endif          
        await _ckSerializer.SerializeAsync(streamWriterAttribute,
            new CkElementsRootDto { Attributes = new List<CkAttributeDto> { ckAttributeDto } });

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

    /// <inheritdoc />
    public async Task CompileAsync(string rootPath, bool createCacheFile)
    {
        ArgumentValidation.ValidateDirectoryPath(nameof(rootPath), rootPath);

        OperationResult operationResult = new OperationResult();

        if (!Directory.Exists(rootPath))
        {
            operationResult.AddMessage(MessageCodes.DirectoryDoesNotExist(rootPath));
            throw CompilerException.DirectoryDoesNotExist(rootPath, operationResult);
        }

        var typesDirectory = Path.Combine(rootPath, CompilerStatics.TypesDirectoryName);
        var attributesDirectory = Path.Combine(rootPath, CompilerStatics.AttributesDirectoryName);
        var associationsDirectory = Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName);

        var modelPath = Path.Combine(rootPath, CompilerStatics.MetadataFile);
        if (!File.Exists(modelPath))
        {
            operationResult.AddMessage(MessageCodes.FileDoesNotExist(modelPath));
            throw CompilerException.FileDoesNotExist(modelPath, operationResult);
        }

#if NETSTANDARD2_0
        using var stream = File.OpenRead(modelPath);
#else
        await using var stream = File.OpenRead(modelPath);
#endif          
        var ckMetaDto = await _ckSerializer.DeserializeMetaAsync(stream, operationResult);

        var types = new List<CkTypeDto>();
        if (Directory.Exists(typesDirectory))
        {
            foreach (var typeFile in Directory.EnumerateFiles(typesDirectory, "*.yaml"))
            {
#if NETSTANDARD2_0
                using var streamType = File.OpenRead(typeFile);
#else
                await using var streamType = File.OpenRead(typeFile);
#endif                 
                var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamType, operationResult);
                if (elementsRootDto.Types != null)
                {
                    types.AddRange(elementsRootDto.Types);
                }
            }
        }

        var attributes = new List<CkAttributeDto>();
        if (Directory.Exists(attributesDirectory))
        {
            foreach (var attributeFile in Directory.EnumerateFiles(attributesDirectory, "*.yaml"))
            {
#if NETSTANDARD2_0
                using var streamAttribute = File.OpenRead(attributeFile);
#else
                await using var streamAttribute = File.OpenRead(attributeFile);
#endif                  
                var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAttribute, operationResult);
                if (elementsRootDto.Attributes != null)
                {
                    attributes.AddRange(elementsRootDto.Attributes);
                }
            }
        }

        var associationRoles = new List<CkAssociationRoleDto>();
        if (Directory.Exists(associationsDirectory))
        {
            foreach (var associationFile in Directory.EnumerateFiles(associationsDirectory, "*.yaml"))
            {
#if NETSTANDARD2_0
                using var streamAssociation = File.OpenRead(associationFile);
#else
                await using var streamAssociation = File.OpenRead(associationFile);
#endif                
                var elementsRootDto = await _ckSerializer.DeserializeElementsAsync(streamAssociation, operationResult);
                if (elementsRootDto.AssociationRoles != null)
                {
                    associationRoles.AddRange(elementsRootDto.AssociationRoles);
                }
            }
        }
        
        CkCompiledModelRoot compiledModelRoot = new CkCompiledModelRoot
        {
            ModelId = ckMetaDto.ModelId,
            Dependencies = ckMetaDto.Dependencies,
            Types = types,
            Attributes = attributes,
            AssociationRoles = associationRoles
        };

        await _ckValidationService.ValidateAsync(compiledModelRoot, operationResult);
        
        if (operationResult.HasErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }
        
        string compiledModelFile = $"ck-{ckMetaDto.ModelId.SemanticVersionedFullName.ToLower()}.yaml";
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelFile));
#else
        await using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelFile));
#endif
        await _ckSerializer.SerializeAsync(streamWriter, compiledModelRoot);
        
        if (createCacheFile)
        {
            await CreateCacheFileAsync(compiledModelRoot, rootPath, operationResult);
        }
    }

    private async Task CreateCacheFileAsync(CkCompiledModelRoot compiledModelRoot, string rootPath, OperationResult operationResult)
    {
        var tempTenantId = Guid.NewGuid().ToString();
        _ckCacheService.CreateTenant(tempTenantId);
        await _ckCacheService.LoadCkModelAsync(tempTenantId, compiledModelRoot, operationResult);
        if (operationResult.HasErrors)
        {
            throw CompilerException.OperationResultWithErrors(operationResult);
        }
        
        string compiledModelCacheFile = $"ck-{compiledModelRoot.ModelId.SemanticVersionedFullName.ToLower()}.cache.json";
#if NETSTANDARD2_0
        using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelCacheFile));
#else
        await using var streamWriter = new StreamWriter(Path.Combine(rootPath, compiledModelCacheFile));
#endif
        await _ckCacheService.SaveCacheAsync(tempTenantId, streamWriter.BaseStream);
    }
}
     