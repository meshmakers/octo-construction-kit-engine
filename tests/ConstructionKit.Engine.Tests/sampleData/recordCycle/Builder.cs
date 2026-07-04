using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.recordCycle;

/// <summary>
/// Model with a record that (transitively) contains itself: Demo1 -> Node -> Node -> ...
/// Used to verify the query column collector detects record cycles instead of recursing
/// until stack overflow.
/// </summary>
public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("TestRecordCycle", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            Attributes =
            [
                new()
                {
                    AttributeId = "Attribute1",
                    ValueType = AttributeValueTypesDto.String
                },

                new()
                {
                    AttributeId = "SelfRecord",
                    ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = "TestRecordCycle/Node"
                }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Node",
                    Attributes =
                    [
                        new() { CkAttributeId = "TestRecordCycle/Attribute1", AttributeName = "MyValue" },
                        new() { CkAttributeId = "TestRecordCycle/SelfRecord", AttributeName = "MyChild" }
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
                        new() { CkAttributeId = "TestRecordCycle/Attribute1", AttributeName = "MyAttributeA" },
                        new() { CkAttributeId = "TestRecordCycle/SelfRecord", AttributeName = "MyRecord" }
                    ]
                }
            ]
        };
    }
}
