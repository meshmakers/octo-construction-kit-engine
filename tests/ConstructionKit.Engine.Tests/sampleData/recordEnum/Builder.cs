using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.recordEnum;

/// <summary>
/// Sample with an enum attribute nested inside a record (mirrors the real-world
/// <c>amount.unit</c> / <c>UnitOfMeasure</c> shape). Used to pin that record descent in
/// <c>CkTypeQueryColumnCollector</c> preserves the enum id on the nested query column, so
/// enum-name resolution works for nested paths and not only top-level enum attributes.
/// </summary>
public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestRecordEnum", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "Related", InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N, InboundName = "Related", OutboundName = "Related"
                }
            ],
            Attributes =
            [
                new()
                {
                    AttributeId = "Value",
                    ValueType = AttributeValueTypesDto.Double
                },

                new()
                {
                    AttributeId = "Unit",
                    ValueType = AttributeValueTypesDto.Enum,
                    ValueCkEnumId = "TestRecordEnum/UnitOfMeasure"
                },

                new()
                {
                    AttributeId = "Amount",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "TestRecordEnum/Amount"
                }
            ],
            Enums =
            [
                new()
                {
                    EnumId = "UnitOfMeasure",
                    Values = new List<CkEnumValueDto>
                    {
                        new() { Key = 0, Name = "None" },
                        new() { Key = 1, Name = "KWh" },
                        new() { Key = 2, Name = "MWh" }
                    }
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Amount",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestRecordEnum/Value", AttributeName = "Value" },
                        new() { CkAttributeId = "TestRecordEnum/Unit", AttributeName = "Unit" }
                    ]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Demo1",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestRecordEnum/Amount", AttributeName = "Amount" }
                    ]
                }
            ]
        };
    }
}
