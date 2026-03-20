using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.withDataStream;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("StreamTest", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            Attributes =
            [
                new()
                {
                    AttributeId = "Temperature",
                    ValueType = AttributeValueTypesDto.Double,
                    IsDataStream = true
                },
                new()
                {
                    AttributeId = "Pressure",
                    ValueType = AttributeValueTypesDto.Double,
                    IsDataStream = true
                },
                new()
                {
                    AttributeId = "DeviceName",
                    ValueType = AttributeValueTypesDto.String,
                    IsDataStream = false
                },
                new()
                {
                    AttributeId = "Location",
                    ValueType = AttributeValueTypesDto.String
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Sensor",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "StreamTest/Temperature", AttributeName = "Temperature" },
                        new() { CkAttributeId = "StreamTest/Pressure", AttributeName = "Pressure" },
                        new() { CkAttributeId = "StreamTest/DeviceName", AttributeName = "DeviceName" },
                        new() { CkAttributeId = "StreamTest/Location", AttributeName = "Location" }
                    ]
                }
            ]
        };
    }
}
