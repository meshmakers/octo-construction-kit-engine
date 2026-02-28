using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.withDescriptions;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("Described", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "ParentLink",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    InboundName = "LinkedBy",
                    OutboundName = "Parent"
                }
            ],
            Attributes =
            [
                new()
                {
                    AttributeId = "MeterReading",
                    ValueType = AttributeValueTypesDto.Double,
                    Description = "Current meter reading value in kWh"
                },
                new()
                {
                    AttributeId = "SerialNumber",
                    ValueType = AttributeValueTypesDto.String,
                    Description = "Unique serial number of the device"
                },
                new()
                {
                    AttributeId = "Status",
                    ValueType = AttributeValueTypesDto.String
                },
                new()
                {
                    AttributeId = "RecordAttr",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "Described/Location"
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Location",
                    Attributes =
                    [
                        new() { CkAttributeId = "Described/SerialNumber", AttributeName = "City" },
                        new() { CkAttributeId = "Described/Status", AttributeName = "Country" }
                    ]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Device",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "Described/MeterReading", AttributeName = "MeterReading" },
                        new() { CkAttributeId = "Described/SerialNumber", AttributeName = "SerialNumber" },
                        new() { CkAttributeId = "Described/Status", AttributeName = "Status" },
                        new() { CkAttributeId = "Described/RecordAttr", AttributeName = "Location" }
                    ],
                    Associations =
                    [
                        new() { CkRoleId = "Described/ParentLink", TargetCkTypeId = "Described/Device" }
                    ]
                }
            ]
        };
    }
}
