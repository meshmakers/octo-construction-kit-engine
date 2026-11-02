using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.hiddenColumns;

/// <summary>
///     CK language 2 model with normal, ReadOnly, MethodOnly, Hidden and Secret attributes on a type, inside a
///     record, inside a record array, as a whole Hidden record and on a navigation target. Used to pin that the
///     query column collector omits Hidden and Secret attributes from the default column set on every level.
///     Account --Owner (0..1)--> Owner
/// </summary>
public class Builder
{
    private const string ModelName = "HiddenColumns";

    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(ModelName, "1.0.0"),
            CkLanguage = 2,
            Dependencies = [new("System", "1.0.0")],
            AssociationRoles =
            [
                new()
                {
                    AssociationRoleId = "Owner",
                    InboundMultiplicity = MultiplicitiesDto.N,
                    OutboundMultiplicity = MultiplicitiesDto.ZeroOrOne,
                    InboundName = "OwnedAccounts",
                    OutboundName = "Owner"
                }
            ],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Email", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Status", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "PasswordHash", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Tags", ValueType = AttributeValueTypesDto.StringArray },
                new() { AttributeId = "ApiKey", ValueType = AttributeValueTypesDto.Secret },
                new() { AttributeId = "Label", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Token", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Profile", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Profile" },
                new() { AttributeId = "Credentials", ValueType = AttributeValueTypesDto.RecordArray, ValueCkRecordId = $"{ModelName}/Profile" },
                new() { AttributeId = "Recovery", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Profile" }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Profile",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Label", AttributeName = "Label", IsOptional = true },
                        new() { CkAttributeId = $"{ModelName}/Token", AttributeName = "Token", IsOptional = true, Access = CkAttributeAccessDto.Hidden },
                        new() { CkAttributeId = $"{ModelName}/ApiKey", AttributeName = "ApiKey", IsOptional = true }
                    ]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Owner",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{ModelName}/PasswordHash", AttributeName = "PasswordHash", IsOptional = true, Access = CkAttributeAccessDto.Hidden }
                    ]
                },
                new()
                {
                    TypeId = "Account",
                    DerivedFromCkTypeId = "System/Entity",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Name", AttributeName = "Name", Access = CkAttributeAccessDto.ReadWrite },
                        new() { CkAttributeId = $"{ModelName}/Email", AttributeName = "Email", IsOptional = true, Access = CkAttributeAccessDto.ReadOnly },
                        new() { CkAttributeId = $"{ModelName}/Status", AttributeName = "Status", IsOptional = true, Access = CkAttributeAccessDto.MethodOnly },
                        new() { CkAttributeId = $"{ModelName}/PasswordHash", AttributeName = "PasswordHash", IsOptional = true, Access = CkAttributeAccessDto.Hidden },
                        new() { CkAttributeId = $"{ModelName}/Tags", AttributeName = "Tags", IsOptional = true, Access = CkAttributeAccessDto.Hidden },
                        new() { CkAttributeId = $"{ModelName}/ApiKey", AttributeName = "ApiKey", IsOptional = true },
                        new() { CkAttributeId = $"{ModelName}/Profile", AttributeName = "Profile", IsOptional = true },
                        new() { CkAttributeId = $"{ModelName}/Credentials", AttributeName = "Credentials", IsOptional = true },
                        new() { CkAttributeId = $"{ModelName}/Recovery", AttributeName = "Recovery", IsOptional = true, Access = CkAttributeAccessDto.Hidden }
                    ],
                    Associations =
                    [
                        new() { CkRoleId = $"{ModelName}/Owner", TargetCkTypeId = $"{ModelName}/Owner" }
                    ]
                }
            ]
        };
    }
}
