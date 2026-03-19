using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.nToMAssociations;

public class Builder
{
    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId("NToMTest", "1.0.0"),
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "TransactionToDocument",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.N,
                    InboundName = "Documents",
                    OutboundName = "Transactions"
                },
                new()
                {
                    AssociationRoleId = "CategoryLink",
                    InboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    OutboundMultiplicity = MultiplicitiesDto.N,
                    InboundName = "Category",
                    OutboundName = "Items"
                }
            ],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Amount", ValueType = AttributeValueTypesDto.Double }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Document",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "NToMTest/Name", AttributeName = "Name" }
                    ],
                    Associations =
                    [
                        new() { CkRoleId = "NToMTest/TransactionToDocument", TargetCkTypeId = "NToMTest/Transaction" }
                    ]
                },
                new()
                {
                    TypeId = "Transaction",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "NToMTest/Name", AttributeName = "Name" },
                        new() { CkAttributeId = "NToMTest/Amount", AttributeName = "Amount" }
                    ]
                },
                new()
                {
                    TypeId = "Category",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = "NToMTest/Name", AttributeName = "Name" }
                    ],
                    Associations =
                    [
                        new() { CkRoleId = "NToMTest/CategoryLink", TargetCkTypeId = "NToMTest/Document" }
                    ]
                }
            ]
        };
    }
}
