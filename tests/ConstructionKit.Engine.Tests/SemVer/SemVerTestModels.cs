using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     Builds minimal compiled model fixtures for the SemVer diff/classifier tests. Every call
///     returns a fresh instance so tests can freely mutate baseline and current independently.
///     The model references the foreign model "Base" so both self and foreign reference
///     semantics are covered.
/// </summary>
internal static class SemVerTestModels
{
    public const string ModelName = "TestModel";

    public static CkCompiledModelRoot CreateModel(string version = "1.0.0")
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(ModelName, version),
            Description = "A test model",
            Dependencies = [new CkModelId("Base", "1.2.3")],
            Types =
            [
                new CkCompiledTypeDto
                {
                    TypeId = "Machine",
                    DerivedFromCkTypeId = "Base/Entity",
                    Description = "A machine",
                    Attributes =
                    [
                        new CkTypeAttributeDto
                        {
                            CkAttributeId = $"{ModelName}/SerialNumber", AttributeName = "SerialNumber",
                            IsOptional = false
                        },
                        new CkTypeAttributeDto
                        {
                            CkAttributeId = $"{ModelName}/StateAttr", AttributeName = "State", IsOptional = true
                        }
                    ],
                    Associations =
                    [
                        new CkTypeAssociationDto
                        {
                            CkRoleId = $"{ModelName}/Parent", TargetCkTypeId = $"{ModelName}/Machine"
                        }
                    ],
                    Indexes =
                    [
                        new CkTypeIndexDto
                        {
                            IndexType = IndexTypeDto.Ascending,
                            Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }]
                        }
                    ]
                }
            ],
            Attributes =
            [
                new CkAttributeDto
                {
                    AttributeId = "SerialNumber", ValueType = AttributeValueTypesDto.String, Description = "Serial"
                },
                new CkAttributeDto
                {
                    AttributeId = "StateAttr", ValueType = AttributeValueTypesDto.Enum,
                    ValueCkEnumId = $"{ModelName}/State"
                },
                new CkAttributeDto
                {
                    AttributeId = "WithDefault", ValueType = AttributeValueTypesDto.Int, DefaultValues = [42]
                }
            ],
            Enums =
            [
                new CkEnumDto
                {
                    EnumId = "State",
                    Values =
                    [
                        new CkEnumValueDto { Key = 0, Name = "Off" },
                        new CkEnumValueDto { Key = 1, Name = "On" }
                    ]
                }
            ],
            Records =
            [
                new CkRecordDto
                {
                    RecordId = "Address",
                    Attributes =
                    [
                        new CkTypeAttributeDto
                        {
                            CkAttributeId = $"{ModelName}/SerialNumber", AttributeName = "Street", IsOptional = true
                        }
                    ]
                }
            ],
            AssociationRoles =
            [
                new CkAssociationRoleDto
                {
                    AssociationRoleId = "Parent",
                    InboundName = "Children",
                    OutboundName = "Parent",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne
                }
            ]
        };
    }

    public static CkCompiledTypeDto GetMachine(CkCompiledModelRoot model)
    {
        return model.Types!.Single(t => t.TypeId.Name == "Machine");
    }

    public static CkAttributeDto GetAttribute(CkCompiledModelRoot model, string name)
    {
        return model.Attributes!.Single(a => a.AttributeId.Name == name);
    }

    public static CkEnumDto GetEnum(CkCompiledModelRoot model)
    {
        return model.Enums!.Single();
    }

    public static CkRecordDto GetRecord(CkCompiledModelRoot model)
    {
        return model.Records!.Single();
    }

    public static CkAssociationRoleDto GetRole(CkCompiledModelRoot model)
    {
        return model.AssociationRoles!.Single();
    }
}
